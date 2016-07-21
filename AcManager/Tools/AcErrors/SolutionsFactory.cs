﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AcManager.Controls.Dialogs;
using AcManager.Pages.Dialogs;
using AcManager.Pages.Selected;
using AcManager.Tools.AcErrors.Solutions;
using AcManager.Tools.AcObjectsNew;
using AcManager.Tools.Helpers;
using AcManager.Tools.Managers;
using AcManager.Tools.Objects;
using AcTools.Kn5File;
using AcTools.Utils.Helpers;
using Newtonsoft.Json;

namespace AcManager.Tools.AcErrors {
    public class SolutionsFactory : ISolutionsFactory {
        IEnumerable<ISolution> ISolutionsFactory.GetSolutions(AcError error) {
            switch (error.Type) {
                case AcErrorType.Load_Base:
                    return null;

                case AcErrorType.Data_JsonIsMissing:
                    return new[] {
                        Solve.TryToCreateNewFile((AcJsonObjectNew)error.Target)
                    }.Concat(Solve.TryToFindRenamedFile(error.Target.Location, ((AcJsonObjectNew)error.Target).JsonFilename)).Where(x => x != null);

                case AcErrorType.Data_JsonIsDamaged:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_RestoreJsonFile,
                                AcManager.Resources.Solution_RestoreJsonFile_Details,
                                e => {
                                    var t = (AcJsonObjectNew)e.Target;
                                    if (!Solve.TryToRestoreDamagedJsonFile(t.JsonFilename, JObjectRestorationSchemeProvider.GetScheme(t))) {
                                        throw new SolvingException(AcManager.Resources.Solution_CannotRestoreJsonFile);
                                    }
                                }),
                        Solve.TryToCreateNewFile((AcJsonObjectNew)error.Target)
                    }.Concat(Solve.TryToFindRenamedFile(error.Target.Location, ((AcJsonObjectNew)error.Target).JsonFilename)).Where(x => x != null);

                case AcErrorType.Data_ObjectNameIsMissing:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_SetName,
                                AcManager.Resources.Solution_SetName_Details,
                                e => {
                                    var value = Prompt.Show(AcManager.Resources.Solution_SetName_Prompt, AcManager.Resources.Common_NewName,
                                            AcStringValues.NameFromId(e.Target.Id), maxLength: 200);
                                    if (value == null) throw new SolvingException();
                                    e.Target.NameEditable = value;
                                }) { IsUiSolution = true },
                        new MultiSolution(
                                AcManager.Resources.Solution_SetNameFromId,
                                string.Format(AcManager.Resources.Solution_SetNameFromId_Details, AcStringValues.NameFromId(error.Target.Id)),
                                e => {
                                    e.Target.NameEditable = AcStringValues.NameFromId(e.Target.Id);
                                }),
                    };

                case AcErrorType.Data_CarBrandIsMissing: {
                    var guess = AcStringValues.BrandFromName(error.Target.DisplayName);
                    return new[] {
                        new Solution(
                                AcManager.Resources.Solution_SetBrandName,
                                AcManager.Resources.Solution_SetBrandName_Details,
                                e => {
                                    var value = Prompt.Show(AcManager.Resources.Solution_SetBrandName_Prompt, AcManager.Resources.Common_NewBrandName, guess,
                                            maxLength: 200,
                                            suggestions: SuggestionLists.CarBrandsList);
                                    if (value == null) throw new SolvingException();
                                    ((CarObject)e.Target).Brand = value;
                                }) { IsUiSolution = true },
                        guess == null ? null : new Solution(
                                AcManager.Resources.Solution_SetBrandNameFromName,
                                string.Format(AcManager.Resources.Solution_SetBrandNameFromName_Details, guess),
                                e => {
                                    ((CarObject)e.Target).Brand = guess;
                                })
                    }.NonNull();
                }

                case AcErrorType.Data_IniIsMissing:
                    return Solve.TryToFindRenamedFile(error.Target.Location, ((AcIniObject)error.Target).IniFilename).Union(new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_RemoveObject,
                                AcManager.Resources.Solution_RemoveObject_Details,
                                e => {
                                    e.Target.DeleteCommand.Execute(null);
                                })
                    });

                case AcErrorType.Weather_ColorCurvesIniIsMissing:
                    return Solve.TryToFindRenamedFile(error.Target.Location, ((WeatherObject)error.Target).ColorCurvesIniFilename).Union(new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_GenerateNew,
                                AcManager.Resources.Solution_GenerateNew_Details,
                                e => {
                                    File.WriteAllText(((WeatherObject)e.Target).ColorCurvesIniFilename, "");
                                }),
                        new MultiSolution(
                                AcManager.Resources.Solution_RemoveObject,
                                AcManager.Resources.Solution_RemoveObject_Details,
                                e => {
                                    e.Target.DeleteCommand.Execute(null);
                                })
                    });

                case AcErrorType.Data_IniIsDamaged:
                    return Solve.TryToFindRenamedFile(error.Target.Location, ((AcIniObject)error.Target).IniFilename).Union(new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_RemoveObject,
                                AcManager.Resources.Solution_RemoveObject_Details,
                                e => {
                                    e.Target.DeleteCommand.Execute(null);
                                })
                    });

                case AcErrorType.Data_UiDirectoryIsMissing:
                    // TODO
                    break;

                case AcErrorType.Car_ParentIsMissing:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_MakeIndependent,
                                AcManager.Resources.Solution_MakeIndependent_Details,
                                e => {
                                    ((CarObject)e.Target).ParentId = null;
                                }),
                        new MultiSolution(
                                AcManager.Resources.Solution_ChangeParent,
                                AcManager.Resources.Solution_ChangeParent_Details,
                                e => {
                                    var target = (CarObject)e.Target;
                                    new ChangeCarParentDialog(target).ShowDialog();
                                    if (target.Parent == null) {
                                        throw new SolvingException();
                                    }
                                }) { IsUiSolution = true }
                    }.Concat(Solve.TryToFindRenamedFile(error.Target.Location, ((AcJsonObjectNew)error.Target).JsonFilename)).NonNull();

                case AcErrorType.Car_BrandBadgeIsMissing: {
                    var car = (CarObject)error.Target;
                    var fit = FilesStorage.Instance.GetContentFile(ContentCategory.BrandBadges, $"{car.Brand}.png");
                    return new ISolution[] {
                        fit.Exists ? new MultiSolution(
                                string.Format(AcManager.Resources.Solution_SetBrandBadge, car.Brand),
                                AcManager.Resources.Solution_SetBrandBadge_Details,
                                e => {
                                    var c = (CarObject)e.Target;
                                    var f = FilesStorage.Instance.GetContentFile(ContentCategory.BrandBadges, $"{c.Brand}.png");
                                    if (!f.Exists) return;
                                    File.Copy(f.Filename, c.BrandBadge);
                                }) : null,
                        new MultiSolution(
                                AcManager.Resources.Solution_ChangeBrandBadge,
                                AcManager.Resources.Solution_ChangeBrandBadge_Details,
                                e => {
                                    var target = (CarObject)e.Target;
                                    new BrandBadgeEditor(target).ShowDialog();
                                    if (!File.Exists(target.BrandBadge)) {
                                        throw new SolvingException();
                                    }
                                }) { IsUiSolution = true }
                    }.Concat(Solve.TryToFindRenamedFile(error.Target.Location, ((CarObject)error.Target).BrandBadge)).NonNull();
                }

                case AcErrorType.Car_UpgradeIconIsMissing: {
                    var car = (CarObject)error.Target;
                    var label = UpgradeIconEditor.TryToGuessLabel(car.DisplayName) ?? @"S1";
                    var fit = FilesStorage.Instance.GetContentFile(ContentCategory.UpgradeIcons, $"{label}.png");
                    return new ISolution[] {
                        fit.Exists ? new MultiSolution(
                                string.Format(AcManager.Resources.Solution_SetUpgradeIcon, label),
                                AcManager.Resources.Solution_SetUpgradeIcon_Details,
                                e => {
                                    var c = (CarObject)e.Target;
                                    var l = UpgradeIconEditor.TryToGuessLabel(c.DisplayName) ?? @"S1";
                                    var f = FilesStorage.Instance.GetContentFile(ContentCategory.UpgradeIcons, $"{l}.png");
                                    if (!f.Exists) return;
                                    File.Copy(f.Filename, c.UpgradeIcon);
                                }) : null,
                        new MultiSolution(
                                AcManager.Resources.Solution_ChangeUpgradeIcon,
                                AcManager.Resources.Solution_ChangeUpgradeIcon_Details,
                                e => {
                                    var target = (CarObject)e.Target;
                                    new UpgradeIconEditor(target).ShowDialog();
                                    if (!File.Exists(target.UpgradeIcon)) {
                                        throw new SolvingException();
                                    }
                                }) { IsUiSolution = true }
                    }.Concat(Solve.TryToFindRenamedFile(error.Target.Location, ((CarObject)error.Target).UpgradeIcon)).NonNull();
                }

                case AcErrorType.Showroom_Kn5IsMissing:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_MakeEmptyModel,
                                AcManager.Resources.Solution_MakeEmptyModel_Details,
                                e => {
                                    Kn5.CreateEmpty().SaveAll(((ShowroomObject)e.Target).Kn5Filename);
                                })
                    }.Concat(Solve.TryToFindAnyFile(error.Target.Location, ((ShowroomObject)error.Target).Kn5Filename, @"*.kn5")).Where(x => x != null);

                case AcErrorType.Data_KunosCareerEventsAreMissing:
                    break;
                case AcErrorType.Data_KunosCareerConditions:
                    break;
                case AcErrorType.Data_KunosCareerContentIsMissing:
                    break;
                case AcErrorType.Data_KunosCareerTrackIsMissing:
                    break;
                case AcErrorType.Data_KunosCareerCarIsMissing:
                    break;
                case AcErrorType.Data_KunosCareerCarSkinIsMissing:
                    break;
                case AcErrorType.Data_KunosCareerWeatherIsMissing:
                    break;

                case AcErrorType.CarSkins_SkinsAreMissing:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_CreateEmptySkin,
                                AcManager.Resources.Solution_CreateEmptySkin_Details,
                                e => {
                                    var target = Path.Combine(((CarObject)e.Target).SkinsDirectory, "default");
                                    Directory.CreateDirectory(target);
                                    File.WriteAllText(Path.Combine(target, "ui_skin.json"), JsonConvert.SerializeObject(new {
                                        skinname = @"Default",
                                        drivername = "",
                                        country = "",
                                        team = "",
                                        number = 0
                                    }));
                                })
                    }.Union(((CarObject)error.Target).SkinsManager.WrappersList.Where(x => !x.Value.Enabled).Select(x => new MultiSolution(
                            string.Format(AcManager.Resources.Solution_EnableSkin, x.Value.DisplayName),
                            AcManager.Resources.Solution_EnableSkin_Details,
                            (IAcError e) => {
                                ((CarSkinObject)x.Loaded()).ToggleCommand.Execute(null);
                            }
                            )))
                     .Concat(Solve.TryToFindRenamedFile(error.Target.Location, ((CarObject)error.Target).SkinsDirectory, true)).NonNull();

                case AcErrorType.CarSkins_DirectoryIsUnavailable:
                    return null;

                case AcErrorType.Font_BitmapIsMissing:
                    return Solve.TryToFindRenamedFile(Path.GetDirectoryName(error.Target.Location), ((FontObject)error.Target).FontBitmap);

                case AcErrorType.Font_UsedButDisabled:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_Enable,
                                AcManager.Resources.Solution_Enable_Details,
                                e => {
                                    e.Target.ToggleCommand.Execute(null);
                                })
                    };

                case AcErrorType.CarSetup_TrackIsMissing:
                    return new[] {
                        new Solution(
                                AcManager.Resources.Solution_FindTrack,
                                AcManager.Resources.Solution_FindTrack_Details,
                                e => {
                                    Process.Start($@"http://assetto-db.com/track/{((CarSetupObject)e.Target).TrackId}");
                                }),
                        new MultiSolution(
                                AcManager.Resources.Solution_MakeGeneric,
                                AcManager.Resources.Solution_MakeGeneric_Details,
                                e => {
                                    ((CarSetupObject)e.Target).TrackId = null;
                                })
                    };

                case AcErrorType.CarSkin_PreviewIsMissing:
                    return new ISolution[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_GeneratePreview,
                                AcManager.Resources.Solution_GeneratePreview_Details,
                                e => {
                                    var list = e.ToList();
                                    var carId = ((CarSkinObject)list[0].Target).CarId;
                                    var skinIds = list.Select(x => x.Target.Id).ToArray();
                                    if (!new CarUpdatePreviewsDialog(CarsManager.Instance.GetById(carId), skinIds,
                                            SelectedCarPage.ViewModel.GetAutoUpdatePreviewsDialogMode()).ShowDialog()) {
                                        throw new SolvingException();
                                    }
                                }) { IsUiSolution = true },
                        new MultiSolution(
                                AcManager.Resources.Solution_SetupPreview,
                                AcManager.Resources.Solution_SetupPreview_Details,
                                e => {
                                    var list = e.ToList();
                                    var carId = ((CarSkinObject)list[0].Target).CarId;
                                    var skinIds = list.Select(x => x.Target.Id).ToArray();
                                    if (!new CarUpdatePreviewsDialog(CarsManager.Instance.GetById(carId), skinIds,
                                            CarUpdatePreviewsDialog.DialogMode.Options).ShowDialog()) {
                                        throw new SolvingException();
                                    }
                                }) { IsUiSolution = true }
                    };

                case AcErrorType.CarSkin_LiveryIsMissing:
                    return new ISolution[] {
                        new AsyncMultiSolution(
                                AcManager.Resources.Solution_GenerateLivery,
                                AcManager.Resources.Solution_GenerateLivery_Details,
                                e => LiveryIconEditor.GenerateAsync((CarSkinObject)e.Target)),
                        new AsyncMultiSolution(
                                AcManager.Resources.Solution_RandomLivery,
                                AcManager.Resources.Solution_RandomLivery_Details,
                                e => LiveryIconEditor.GenerateRandomAsync((CarSkinObject)e.Target)),
                        new MultiSolution(
                                AcManager.Resources.Solution_SetupLivery,
                                AcManager.Resources.Solution_SetupLivery_Details,
                                e => {
                                    if (!new LiveryIconEditor((CarSkinObject)e.Target).ShowDialog()) {
                                        throw new SolvingException();
                                    }
                                }) { IsUiSolution = true }
                    };


                case AcErrorType.Replay_TrackIsMissing:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_RemoveReplay,
                                AcManager.Resources.Solution_RemoveReplay_Details,
                                e => {
                                    e.Target.DeleteCommand.Execute(null);
                                })
                    };

                case AcErrorType.Replay_InvalidName:
                    return new[] {
                        new MultiSolution(
                                AcManager.Resources.Solution_FixName,
                                AcManager.Resources.Solution_FixName_Details,
                                e => {
                                    e.Target.NameEditable = Regex.Replace(e.Target.NameEditable ?? @"-", @"[\[\]]", "");
                                })
                    };

                default:
                    return null;
            }

            return null;
        }
    }
}
