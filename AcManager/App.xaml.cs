﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AcManager.Controls;
using AcManager.Controls.CustomShowroom;
using AcManager.Controls.Dialogs;
using AcManager.Controls.Helpers;
using AcManager.Controls.Presentation;
using AcManager.Controls.ViewModels;
using AcManager.Internal;
using AcManager.Pages.Dialogs;
using AcManager.Pages.Windows;
using AcManager.Plugins;
using AcManager.Properties;
using AcManager.Tools;
using AcManager.Tools.AcErrors;
using AcManager.Tools.AcManagersNew;
using AcManager.Tools.Data;
using AcManager.Tools.GameProperties;
using AcManager.Tools.Helpers;
using AcManager.Tools.Helpers.AcSettings;
using AcManager.Tools.Helpers.Api;
using AcManager.Tools.Managers;
using AcManager.Tools.Managers.Plugins;
using AcManager.Tools.Managers.Online;
using AcManager.Tools.Managers.Presets;
using AcManager.Tools.Miscellaneous;
using AcManager.Tools.Objects;
using AcManager.Tools.SemiGui;
using AcManager.Tools.Starters;
using AcTools.DataFile;
using AcTools.Processes;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using FirstFloor.ModernUI.Win32;
using FirstFloor.ModernUI.Windows.Controls;
using Newtonsoft.Json;
using StringBasedFilter;

namespace AcManager {
    public partial class App : FatalErrorMessage.IAppRestartHelper {
        private const string WebBrowserEmulationModeDisabledKey = "___webBrowserEmulationModeDisabled";

        public static void CreateAndRun() {
            FilesStorage.Initialize(EntryPoint.ApplicationDataDirectory);
            if (AppArguments.GetBool(AppFlag.DisableSaving)) {
                ValuesStorage.Initialize();
                CacheStorage.Initialize();
            } else {
                ValuesStorage.Initialize(FilesStorage.Instance.GetFilename("Values.data"), AppArguments.GetBool(AppFlag.DisableValuesCompression));
                CacheStorage.Initialize(FilesStorage.Instance.GetFilename("Cache.data"), AppArguments.GetBool(AppFlag.DisableValuesCompression));
            }

            if (!AppArguments.GetBool(AppFlag.DisableLogging)) {
                var logFilename = EntryPoint.GetLogName("Main Log");
                Logging.Initialize(FilesStorage.Instance.GetFilename("Logs", logFilename), AppArguments.GetBool(AppFlag.OptimizeLogging, true));
                Logging.Write($"App version: {BuildInformation.AppVersion} ({BuildInformation.Platform}, {WindowsVersionHelper.GetVersion()})");
            }

            if (AppArguments.GetBool(AppFlag.IgnoreSystemProxy, true)) {
                WebRequest.DefaultWebProxy = null;
            }

            NonfatalError.Initialize();
            LocaleHelper.InitializeAsync().Wait();
            new App().Run();
        }

        private App() {
            AppArguments.Set(AppFlag.SyncNavigation, ref ModernFrame.OptionUseSyncNavigation);
            AppArguments.Set(AppFlag.DisableTransitionAnimation, ref ModernFrame.OptionDisableTransitionAnimation);
            AppArguments.Set(AppFlag.RecentlyClosedQueueSize, ref LinkGroupFilterable.OptionRecentlyClosedQueueSize);

            AppArguments.Set(AppFlag.ForceSteamId, ref SteamIdHelper.OptionForceValue);
            
            AppArguments.Set(AppFlag.IgnoreSystemProxy, ref KunosApiProvider.OptionIgnoreSystemProxy);
            AppArguments.Set(AppFlag.ScanPingTimeout, ref RecentManagerOld.OptionScanPingTimeout);
            AppArguments.Set(AppFlag.LanSocketTimeout, ref KunosApiProvider.OptionLanSocketTimeout);
            AppArguments.Set(AppFlag.LanPollTimeout, ref KunosApiProvider.OptionLanPollTimeout);
            AppArguments.Set(AppFlag.WebRequestTimeout, ref KunosApiProvider.OptionWebRequestTimeout);
            AppArguments.Set(AppFlag.CommandTimeout, ref GameCommandExecutorBase.OptionCommandTimeout);

            AppArguments.Set(AppFlag.DisableAcRootChecking, ref AcRootDirectory.OptionDisableChecking);
            AppArguments.Set(AppFlag.AcObjectsLoadingConcurrency, ref BaseAcManagerNew.OptionAcObjectsLoadingConcurrency);
            AppArguments.Set(AppFlag.SkinsLoadingConcurrency, ref CarObject.OptionSkinsLoadingConcurrency);
            AppArguments.Set(AppFlag.KunosCareerIgnoreSkippedEvents, ref KunosCareerEventsManager.OptionIgnoreSkippedEvents);
            AppArguments.Set(AppFlag.IgnoreMissingSkinsInKunosEvents, ref KunosEventObjectBase.OptionIgnoreMissingSkins);

            AppArguments.Set(AppFlag.ForceToastFallbackMode, ref Toast.OptionFallbackMode);

            AppArguments.Set(AppFlag.SmartPresetsChangedHandling, ref UserPresetsControl.OptionSmartChangedHandling);
            AppArguments.Set(AppFlag.EnableRaceIniRestoration, ref Game.OptionEnableRaceIniRestoration);
            AppArguments.Set(AppFlag.EnableRaceIniTestMode, ref Game.OptionRaceIniTestMode);
            AppArguments.Set(AppFlag.RaceOutDebug, ref Game.OptionDebugMode);

            AppArguments.Set(AppFlag.LiteStartupModeSupported, ref Pages.Windows.MainWindow.OptionLiteModeSupported);
            AppArguments.Set(AppFlag.NfsPorscheTribute, ref RaceGridViewModel.OptionNfsPorscheNames);
            AppArguments.Set(AppFlag.KeepIniComments, ref IniFile.OptionKeepComments);

            LimitedSpace.Initialize();
            LimitedStorage.Initialize();

            DataProvider.Initialize();

            TestKey();

            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            if (!AppArguments.GetBool(AppFlag.PreventDisableWebBrowserEmulationMode) && (
                    ValuesStorage.GetInt(WebBrowserEmulationModeDisabledKey) < WebBrowserHelper.EmulationModeDisablingVersion ||
                            AppArguments.GetBool(AppFlag.ForceDisableWebBrowserEmulationMode))) {
                try {
                    WebBrowserHelper.DisableBrowserEmulationMode();
                    ValuesStorage.Set(WebBrowserEmulationModeDisabledKey, WebBrowserHelper.EmulationModeDisablingVersion);
                } catch (Exception e) {
                    Logging.Warning("Can’t disable emulation mode: " + e);
                }
            }

            JsonConvert.DefaultSettings = () => new JsonSerializerSettings {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                Culture = CultureInfo.InvariantCulture
            };

            var ignoreControls = AppArguments.Get(AppFlag.IgnoreControls);
            if (!string.IsNullOrWhiteSpace(ignoreControls)) {
                ControlsSettings.OptionIgnoreControlsFilter = Filter.Create(new StringTester(), ignoreControls);
            }

            var sseStart = AppArguments.Get(AppFlag.SseName);
            if (!string.IsNullOrWhiteSpace(sseStart)) {
                SseStarter.OptionStartName = sseStart;
            }

            FancyBackgroundManager.Initialize();
            DpiAwareWindow.OptionScale = AppArguments.GetDouble(AppFlag.UiScale, 1d);
            
            AppArguments.Set(AppFlag.CustomThemes, ref AppAppearanceManager.OptionCustomThemes);
            AppAppearanceManager.OptionIdealFormattingModeDefaultValue = AppArguments.GetBool(AppFlag.IdealFormattingMode,
                    !Equals(DpiAwareWindow.OptionScale, 1d));
            AppAppearanceManager.Initialize();

            AcObjectsUriManager.Register(new UriProvider());

            var uiFactory = new GameWrapperUiFactory();
            GameWrapper.RegisterFactory(uiFactory);
            ServerEntryOld.RegisterFactory(uiFactory);

            GameWrapper.RegisterFactory(new DefaultAssistsFactory());

            AcError.RegisterFixer(new AcErrorFixer());
            AcError.RegisterSolutionsFactory(new SolutionsFactory());

            InitializePresets();

            SharingHelper.Initialize();
            SharingUiHelper.Initialize();

            var addonsDir = FilesStorage.Instance.GetFilename("Addons");
            var pluginsDir = FilesStorage.Instance.GetFilename("Plugins");
            if (Directory.Exists(addonsDir) && !Directory.Exists(pluginsDir)) {
                Directory.Move(addonsDir, pluginsDir);
            } else {
                pluginsDir = FilesStorage.Instance.GetDirectory("Plugins");
            }

            PluginsManager.Initialize(pluginsDir);
            PluginsWrappers.Initialize(
                    new MagickPluginWrapper(),
                    new AwesomiumPluginWrapper(),
                    new StarterPlus());

            SteamIdHelper.Initialize(AppArguments.Get(AppFlag.ForceSteamId));
            Superintendent.Initialize();

            AppArguments.Set(AppFlag.OfflineMode, ref AppKeyDialog.OptionOfflineMode);

            PrepareUi();
            var iconUri = new Uri("pack://application:,,,/Content Manager;component/Assets/Icons/Icon.ico",
                    UriKind.Absolute);
            CustomShowroomWrapper.SetDefaultIcon(iconUri);
            Toast.SetDefaultIcon(iconUri);
            Toast.SetDefaultAction(() => (Current.Windows.OfType<ModernWindow>().FirstOrDefault(x => x.IsActive) ??
                    Current.MainWindow as ModernWindow)?.BringToFront());
            BbCodeBlock.ImageClicked += BbCodeBlock_ImageClicked;

            AppArguments.Set(AppFlag.LoadImagesInBackground, ref BetterImage.OptionBackgroundLoading);
            Filter.OptionSimpleMatching = SettingsHolder.Content.SimpleFiltering;
            
            StartupUri = new Uri(!Superintendent.Instance.IsReady || AcRootDirectorySelector.IsReviewNeeded() ?
                    @"Pages/Dialogs/AcRootDirectorySelector.xaml" : @"Pages/Windows/MainWindow.xaml", UriKind.Relative);

            InitializeUpdatableStuff();
            BackgroundInitialization();

            FatalErrorMessage.Register(this);
            ImageUtils.SafeMagickWrapper = fn => {
                try {
                    return fn();
                } catch (OutOfMemoryException e) {
                    NonfatalError.Notify(ToolsStrings.MagickNet_CannotLoad, ToolsStrings.MagickNet_CannotLoad_Commentary, e);
                } catch (Exception e) {
                    NonfatalError.Notify(ToolsStrings.MagickNet_CannotLoad, e);
                }
                return null;
            };

            AbstractDataFile.ErrorsCatcher = new DataSyntaxErrorCatcher();
        }

        private class DataSyntaxErrorCatcher : ISyntaxErrorsCatcher {
            public void Catch(AbstractDataFile file, int line) {
                if (file.Mode == AbstractDataFile.StorageMode.AcdFile) {
                    NonfatalError.NotifyBackground(string.Format("Syntax error in packed {0} at {1} line", file.UnpackedFilename, line),
                            "File is still parsed, but some values might be skipped.");
                } else {
                    NonfatalError.NotifyBackground(string.Format("Syntax error in {0} at {1} line", Path.GetFileName(file.SourceFilename), line),
                            "File is still parsed, but some values might be skipped.", null, new[] {
                                new INonfatalErrorSolution("Open File", null, token => {
                                    WindowsHelper.OpenFile(file.SourceFilename);
                                    return Task.Delay(0, token);
                                })
                            });
                }
            }
        }

        private void PrepareUi() {
            try {
                ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(true));
                ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(700));
                ItemsControl.IsTextSearchCaseSensitiveProperty.OverrideMetadata(typeof(ComboBox), new FrameworkPropertyMetadata(true));
            } catch (Exception e) {
                Logging.Error(e);
            }

            PopupHelper.Initialize();
        }

        private async void TestKey() {
            AppKeyHolder.Initialize(FilesStorage.Instance.GetFilename("License.txt"));
            if (AppKeyHolder.Instance.Revoked == null) return;

            await Task.Delay(3000);

            ValuesStorage.SetEncrypted(AppKeyDialog.AppKeyRevokedKey, AppKeyHolder.Instance.Revoked);
            AppKeyHolder.Instance.SetKey(null);

            Current.Dispatcher.Invoke(() => {
                if (Current?.MainWindow is MainWindow && Current.MainWindow.IsActive) {
                    AppKeyDialog.ShowRevokedMessage();
                }
            });
        }

        private async void BackgroundInitialization() {
            await Task.Delay(1000);
            if (AppUpdater.JustUpdated && SettingsHolder.Common.ShowDetailedChangelog) {
                List<ChangelogEntry> changelog;
                try {
                    changelog = await Task.Run(() => AppUpdater.LoadChangelog().Where(x => x.Version.IsVersionNewerThan(AppUpdater.PreviousVersion)).ToList());
                } catch (WebException e) {
                    NonfatalError.NotifyBackground("Can’t load changelog", ToolsStrings.Common_MakeSureInternetWorks, e);
                    return;
                } catch (Exception e) {
                    NonfatalError.NotifyBackground("Can’t load changelog", e);
                    return;
                }

                Logging.Debug("Changelog entries: " + changelog.Count);
                if (changelog.Any()) {
                    Toast.Show(AppStrings.App_AppUpdated, AppStrings.App_AppUpdated_Details, () => {
                        ModernDialog.ShowMessage(changelog.Select(x => $"[b]{x.Version}[/b]{Environment.NewLine}{x.Changes}")
                                                          .JoinToString(Environment.NewLine.RepeatString(2)), "Recent Changes", MessageBoxButton.OK);
                    });
                }
            }

            await Task.Delay(1500);
            WeatherSpecificCloudsHelper.Revert();
            WeatherSpecificTyreSmokeHelper.Revert();
            WeatherSpecificPpFilterHelper.Revert();
            CopyFilterToSystemForOculusHelper.Revert();

            await Task.Delay(1500);
            CustomUriSchemeHelper.EnsureRegistered();

            await Task.Delay(5000);
            await Task.Run(() => {
                foreach (var f in from file in Directory.GetFiles(FilesStorage.Instance.GetDirectory("Logs"))
                                  where file.EndsWith(@".txt") || file.EndsWith(@".log") || file.EndsWith(@".json")
                                  let info = new FileInfo(file)
                                  where info.LastWriteTime < DateTime.Now - TimeSpan.FromDays(3)
                                  select info) {
                    f.Delete();
                }
            });
        }

        private void BbCodeBlock_ImageClicked(object sender, BbCodeImageEventArgs e) {
            new ImageViewer(e.ImageUri.ToString()).ShowDialog();
        }

        private void InitializeUpdatableStuff() {
            DataUpdater.Initialize();
            DataUpdater.Instance.Updated += DataUpdater_Updated;

            AppUpdater.Initialize();
            AppUpdater.Instance.Updated += AppUpdater_Updated;

            if (LocaleHelper.JustUpdated) {
                Toast.Show(AppStrings.App_LocaleUpdated, string.Format(AppStrings.App_DataUpdated_Details, LocaleHelper.LoadedVersion));
            }

            LocaleUpdater.Initialize(LocaleHelper.LoadedVersion);
            LocaleUpdater.Instance.Updated += LocaleUpdater_Updated;
        }

        private void DataUpdater_Updated(object sender, EventArgs e) {
            Toast.Show(AppStrings.App_DataUpdated, string.Format(AppStrings.App_DataUpdated_Details, DataUpdater.Instance.InstalledVersion));
        }

        private void AppUpdater_Updated(object sender, EventArgs e) {
            Toast.Show(AppStrings.App_NewVersion,
                    string.Format(AppStrings.App_NewVersion_Details, AppUpdater.Instance.UpdateIsReady), () => {
                        AppUpdater.Instance.FinishUpdateCommand.Execute(null);
                    });
        }

        private void LocaleUpdater_Updated(object sender, EventArgs e) {
            if (string.Equals(CultureInfo.CurrentUICulture.Name, SettingsHolder.Locale.LocaleName, StringComparison.OrdinalIgnoreCase)) {
                Toast.Show(AppStrings.App_LocaleUpdated, AppStrings.App_LocaleUpdated_Details, WindowsHelper.RestartCurrentApplication);
            }
        }

        private static void InitializePresets() {
            PresetsManager.Initialize(FilesStorage.Instance.GetDirectory("Presets"));
            PresetsManager.Instance.RegisterBuiltInPreset(BinaryResources.PresetPreviewsKunos, @"Previews", @"Kunos");
            PresetsManager.Instance.RegisterBuiltInPreset(BinaryResources.AssistsGamer, @"Assists", ControlsStrings.AssistsPreset_Gamer);
            PresetsManager.Instance.RegisterBuiltInPreset(BinaryResources.AssistsIntermediate, @"Assists", ControlsStrings.AssistsPreset_Intermediate);
            PresetsManager.Instance.RegisterBuiltInPreset(BinaryResources.AssistsPro, @"Assists", ControlsStrings.AssistsPreset_Pro);
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e) {
            Logging.Flush();
            Storage.SaveBeforeExit();
            KunosCareerProgress.SaveBeforeExit();
        }

        public void Restart() {
            WindowsHelper.RestartCurrentApplication();
        }
    }
}
