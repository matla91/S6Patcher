using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using MsBox.Avalonia.Enums;
using S6Patcher.Source.Patcher;
using S6Patcher.Source.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace S6Patcher.Source.Views
{
    public partial class MainWindow : Window
    {
        private bool PatchingInProgress = false;
        private Patcher.Patcher MainPatcher = null;
        private readonly GameCustomization GameCustomizer = new();

        private readonly ViewHelpers ViewHelpers;
        private readonly bool UseCheckSumCalculation = true;
        private readonly Dictionary<execID, string[]> Mapping = new()
        {
            {execID.OV,         ["tiGeneral", "tiDev", "tiArchive"]},
            {execID.HE_STEAM,   ["tiGeneral", "tiHistory", "tiDev", "tiArchive"]},
            {execID.HE_UBISOFT, ["tiGeneral", "tiHistory", "tiDev", "tiArchive"]},
            {execID.ED,         ["tiGeneral", "tiEditor", "tiDev", "tiArchive", "cbModDownload", "cbZoom", "cbEasyDebug", "cbFolderPath", "txtResolution", "txtZoom"]}
        };

        public MainWindow()
        {
            InitializeComponent();

            UseCheckSumCalculation &= !Program.CommandLineArguments.Any(arg => arg.Contains("-skipchecksum"));
            Title = "S6Patcher v" + Utility.GetApplicationVersion() + " - Made by Eisenmonoxid";
            ViewHelpers = new ViewHelpers(this);

            Opened += MainWindow_Opened;
        }

        private async void MainWindow_Opened(object sender, EventArgs e)
        {
            Opened -= MainWindow_Opened;

            Backup.ShowMessage += async Message => await ShowMessageBox("Backup", Message);
            Backup.ShowMessagePrompt += async Message => await ShowPromptMessageBoxWrapper("Backup", Message);

            DisableUI(true);

            string AvaloniaVersion = typeof(AvaloniaObject).Assembly.GetName().Version.ToString(3);
            Logger.Instance.Log(Title + " - Avalonia: " + AvaloniaVersion + " - Runtime: " + RuntimeInformation.FrameworkDescription);

            string RegistryPath = Utility.ReadPathFromWindowsRegistry();
            if (!string.IsNullOrEmpty(RegistryPath))
            {
                await ShouldUseRegistryPath(RegistryPath);
            }

            ViewHelpers.CheckForUpdates(true);
        }

        private async Task ShouldUseRegistryPath(string RegistryPath)
        {
            Logger.Instance.Log("Installation path found in registry.");

            RegistryPath = Path.Combine(RegistryPath, "base", "bin", "Settlers6.exe");
            if (!File.Exists(RegistryPath))
            {
                Logger.Instance.Log("Registry installation path does not contain a supported executable.");
                return;
            }

            bool Result = await ShowPromptMessageBoxWrapper("Game Installation Found", 
                "Found Game Installation in Path:\n" + RegistryPath + "\n\nUse this Path?");
            if (Result)
            {
                await InitializePatcher(IOFileHandler.Instance.IsPlayLauncherExecutable(RegistryPath));
            }
        }

        private void EnableUIElements(execID ID)
        {
            ViewHelpers.ViewAccessorWrapper(() =>
            {
                ViewHelpers.TogglePanelVisibility(true);
                ViewHelpers.GetControlsByType<CheckBox>().ToList().ForEach(Result => Result.IsEnabled = true);

                if (ID == execID.ED)
                {
                    cbUpdater.IsEnabled = false;
                }

                if (Mapping.TryGetValue(ID, out string[] Value))
                {
                    var Names = new HashSet<string>(Value);
                    foreach (var Element in ViewHelpers.GetControlsByType<TabItem>())
                    {
                        Element.IsEnabled = Names.Contains(Element.Name);
                    }

                    List<Control> ControlResults =
                        [.. from Entry in Value
                            from Control in ViewHelpers.GetControlsByNames(Value)
                            where Control is not TabItem
                            select Control];

                    ControlResults.ForEach(Result => Result.IsEnabled = false);
                }

                btnPatch.IsEnabled = true;
                btnBackup.IsEnabled = true;
            });
        }

        private void UncheckFeatureBoxes()
        {
            ViewHelpers.ViewAccessorWrapper(() =>
            {
                ViewHelpers.GetControlsByType<CheckBox>().ToList().ForEach(Element => Element.IsChecked = false);
            });
        }

        private void DisableUI(bool Uncheck)
        {
            ViewHelpers.ViewAccessorWrapper(() =>
            {
                tcMain.SelectedIndex = 0;
                if (Uncheck)
                {
                    UncheckFeatureBoxes();
                }
                
                ViewHelpers.GetControlsByType<TabItem>().ToList().ForEach(Element => Element.IsEnabled = false);
                
                btnPatch.IsEnabled = false;
                btnBackup.IsEnabled = false;
                txtPath.Text = "...";
                
                ViewHelpers.TogglePanelVisibility(false);
            });
        }
        
        private async void ChooseDocumentsFolder()
        {
            string Path = await ViewHelpers.GetFolderFromFolderPicker("Choose destination folder");
            if (string.IsNullOrEmpty(Path))
            {
                cbFolderPath.IsChecked = false;
                txtFolderPath.Text = "...";
                txtFolderPath.IsEnabled = false;
                return;
            }

            txtFolderPath.Text = Path;
            txtFolderPath.IsEnabled = true;
        }

        private async void OpenFilePicker()
        {
            DisableUI(true);
            ResetPatcher();

            string Path = await ViewHelpers.GetFileFromFilePicker("Choose .exe file", "Settlers6", ViewHelpers.Executable);
            if (string.IsNullOrEmpty(Path))
            {
                txtPath.Text = "...";
                return;
            }

            Path = IOFileHandler.Instance.IsPlayLauncherExecutable(Path);
            await InitializePatcher(Path);
        }

        private async Task InitializePatcher(string Filepath)
        {
            try
            {
                MainPatcher = new Patcher.Patcher(Filepath);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(ex.ToString());
                await ShowMessageBox("Error", ex.Message);
                return;
            }

            txtPath.Text = Filepath;
            MainPatcher.GlobalMod.ShowMessage += async Message => await ShowMessageBox("ModLoader", Message);
            
            EnableUIElements(MainPatcher.GlobalID);
        }

        private async Task FinishPatching()
        {
            ResetPatcher();
            UncheckFeatureBoxes();

            string Message = "Patching finished!" + (ErrorTracking.Count > 0 ? 
                $"\nThere were {ErrorTracking.Count} errors during the process. Please check the log file for more information." : 
                "\nNo errors occurred during the process.");

            await ShowMessageBox("Finished", Message);
        }

        private async void MainPatchingTask()
        {
            if (PatchingInProgress || MainPatcher == null) return;
            PatchingInProgress = true;
            ErrorTracking.Reset();
            Logger.Instance.Log("Patching Process Started!");

            string PreviousDocuments = UserScriptHandler.Instance.GlobalDocuments;
            bool Completed = false;
            try
            {
                DisableUI(false);
                btnChoose.IsEnabled = false;
                btnExit.IsEnabled = false;

                // Route scripts and settings to the same root that the DCP executable patch will select.
                if (cbFolderPath.IsChecked == true && MainPatcher.GlobalID != execID.ED)
                    UserScriptHandler.Instance.UseDocumentsRoot(txtFolderPath.Text);
                await ViewHelpers.GetPathToOptionsFile();
                if (MainPatcher.GlobalID != execID.ED && !UserScriptHandler.Instance.DoesUserScriptDirectoryExist())
                    throw new IOException("Game documents folder not found. Select its Options.ini file before patching.");
                await PatchByFeatures();
                MainPatcher.Complete(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && UseCheckSumCalculation);
                Completed = true;
                await FinishPatching();
            }
            catch (Exception ex)
            {
                ErrorTracking.Increment();
                Logger.Instance.Log(ex.ToString());
                string Result = Completed
                    ? "\nThe executable was committed, but finishing the interface failed. Check S6Patcher.log before retrying."
                    : "\nThe executable was not committed. Auxiliary files may have been updated; see S6Patcher.log.";
                await ShowMessageBox(Completed ? "Patching completed with an interface error" : "Patching failed", ex.Message + Result);
            }
            finally
            {
                if (!Completed) UserScriptHandler.Instance.GlobalDocuments = PreviousDocuments;
                try { ResetPatcher(); }
                catch (Exception ex)
                {
                    ErrorTracking.Increment();
                    Logger.Instance.Log("Patcher cleanup failed: " + ex);
                }
                finally
                {
                    btnChoose.IsEnabled = true;
                    btnExit.IsEnabled = true;
                    PatchingInProgress = false;
                }
                Logger.Instance.Log($"Patching process ended: executable committed={Completed}; reported errors={ErrorTracking.Count}.");
            }
        }

        private List<string> GetFeatures()
        {
            List<string> Features = [.. ViewHelpers.GetSelectedFeatures()
                .Select(Item => Mappings.UIFeatures.TryGetValue(Item, out string Value) ? Value : null)
                .OfType<string>()];

            if (MainPatcher.GlobalID != execID.ED)
            {
                Features.Add(Mappings.UIFeatures.GetValueOrDefault("cbScriptBugFixes")); // Should always be applied
                Features.Add(Mappings.UIFeatures.GetValueOrDefault("cbLimitedEdition")); // Same here
            }

            Features.ForEach(Element => Logger.Instance.Log("Selected Feature: " + Element));
            return Features;
        }

        private async Task PatchByFeatures()
        {
            bool UseBugfixMod = cbModDownload.IsChecked == true || cbUpdater.IsChecked == true;
            bool UseModLoader = MainPatcher.GlobalID != execID.ED;
            bool DoNotUseEmbedded = rbDownload.IsChecked == true;
            bool UseGameplayModification = cbModding.IsChecked == true;

            // Keep mutations ordered and await each operation before another can fail or close the patcher.
            await PatcherScriptFilesWrapper(DoNotUseEmbedded);
            await PatcherModLoaderWrapper(UseBugfixMod, UseModLoader, DoNotUseEmbedded, UseGameplayModification);

            if (cbUpdater.IsChecked == true)
            {
                return;
            }

            MainPatcher.PatchByControlFeatures(GetFeatures());
            if (cbHighTextures.IsChecked == true && MainPatcher.GlobalID != execID.ED)
            {
                MainPatcher.SetTextureResolution(txtResolution.Text);
            }
            if (cbZoom.IsChecked == true)
            {
                MainPatcher.SetZoomLevel(txtZoom.Text);
            }
            if (cbAutosave.IsChecked == true)
            {
                MainPatcher.SetAutosaveTimer(txtAutosave.Text);
            }
            if (cbLAAFlag.IsChecked == true)
            {
                MainPatcher.SetLargeAddressAwareFlag();
            }
            if (cbEasyDebug.IsChecked == true)
            {
                MainPatcher.SetEasyDebug();
            }
            if (cbFolderPath.IsChecked == true && !string.IsNullOrEmpty(txtFolderPath.Text))
            {
                MainPatcher.SetDocumentsFolderPath(txtFolderPath.Text);
            }

        }

        private async Task PatcherModLoaderWrapper(bool ModInstallation, bool UseModLoader, bool DoNotUseEmbedded, bool UseGamelayModification)
        {
            if (MainPatcher.GlobalID != execID.ED && (UseModLoader || ModInstallation))
            {
                await MainPatcher.SetModLoader(ModInstallation, DoNotUseEmbedded, UseGamelayModification);
            }
        }

        private async Task PatcherScriptFilesWrapper(bool DoNotUseEmbedded)
        {
            if (MainPatcher.GlobalID != execID.ED)
            {
                await MainPatcher.WriteScriptFilesToFolder(DoNotUseEmbedded);
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (PatchingInProgress)
            {
                // Closing streams while awaited work continues could invalidate the staged operation.
                e.Cancel = true;
                return;
            }

            ResetPatcher();
            
            bool UncollectedStreams = IOFileHandler.Instance.AreStreamsOpen();
            Logger.Instance.Log("Shutting Down: Are Streams still open: " + UncollectedStreams.ToString() + ".");

            WebHandler.Instance.Dispose();
            Logger.Instance.Dispose();

            GameCustomizer.CloseForApplicationShutdown();
            base.OnClosing(e);
        }

        private async void RestoreBackup()
        {
            execID GlobalID = MainPatcher?.GlobalID ?? execID.NONE;
            string BackupPath = txtPath.Text;
            
            ResetPatcher();
            DisableUI(true);
            await Backup.Restore(BackupPath, GlobalID);
        }

        private async Task<bool> ShowPromptMessageBoxWrapper(string Title, string Message) => await ViewHelpers.ShowPromptMessageBox(Title, Message) == ButtonResult.Yes;
        private async Task ShowMessageBox(string Title, string Message) => await ViewHelpers.ShowMessageBox(Title, Message);

        private void ResetPatcher()
        {
            var Previous = MainPatcher;
            MainPatcher = null;
            Previous?.Dispose();
        }

        private void btnArchiveUnpack_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => UnpackArchiveFileWrapper();
        private void btnArchivePack_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => PackArchiveFileWrapper();
        private void btnPatch_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => MainPatchingTask();
        private void btnBackup_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => RestoreBackup();
        private void btnChoose_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => OpenFilePicker();
        private void btnUpdate_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => ViewHelpers.CheckForUpdates(false);
        private void btnExit_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
        private void btnCustomizationWindow_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e) => GameCustomizer.ShowDialog(this);
        private void cbUpdater_Checked(object sender, Avalonia.Interactivity.RoutedEventArgs e) => tcMain.IsEnabled = cbUpdater.IsChecked == false;
        private void rbDownload_IsCheckedChanged(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            cbUpdater.IsEnabled = rbDownload.IsChecked == true;
            if (rbDownload.IsChecked == false)
            {
                cbUpdater.IsChecked = false;
            }
        }

        private void cbModDownload_IsCheckedChanged(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (cbModDownload.IsChecked == false)
            {
                cbModding.IsChecked = false;
            }
        }
        
        private void cbHighTextures_IsCheckedChanged(object sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            txtResolution.IsEnabled = cbHighTextures.IsChecked == true && MainPatcher.GlobalID != execID.ED;
        private void cbFolderPath_IsCheckedChanged(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (cbFolderPath.IsChecked == true)
            {
                ChooseDocumentsFolder();
            }
            else
            {
                txtFolderPath.Text = "...";
                txtFolderPath.IsEnabled = false;
            }
        }

        private async void PackArchiveFileWrapper()
        {
            var Panel = this.FindControl<TabItem>("tiArchive");

            Panel?.IsEnabled = false;
            await PackArchiveFile();
            Panel?.IsEnabled = true;
        }

        private async Task PackArchiveFile()
        {
            string Message;
            DirectoryInfo Info;

            string FileExtension = rbBBA.IsChecked == true ? ".bba" : (rbS6MAP.IsChecked == true ? ".s6map" : ".s6xmap");
            string FolderPath = await ViewHelpers.GetFolderFromFolderPicker("Choose folder");
            if (string.IsNullOrEmpty(FolderPath))
            {
                return;
            }

            try
            {
                Info = new DirectoryInfo(FolderPath);
            }
            catch (Exception ex)
            {
                Message = $"An error occured trying to pack the folder {Path.GetDirectoryName(FolderPath)}.\n{ex.Message}";
                await ShowMessageBox("Unpacking Archive File ...", Message);
                return;
            }

            string ArchiveFileName = FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + FileExtension;
            bool Result = await Archives.PackFolderFilesIntoArchiveFileAsync(FolderPath, ArchiveFileName);

            Message = Result ? $"Archive created: {Path.GetFileName(ArchiveFileName)}." : "Archive creation did not complete successfully.";
            Message += "\n\n" + (Result ? "No errors occured!" : "An error occured!");
            await ShowMessageBox("Packing Archive File ...", Message);
        }

        private async void UnpackArchiveFileWrapper()
        {
            var Panel = this.FindControl<TabItem>("tiArchive");

            Panel?.IsEnabled = false;
            await UnpackArchiveFile();
            Panel?.IsEnabled = true;
        }

        private async Task UnpackArchiveFile()
        {
            string FilePath = await ViewHelpers.GetFileFromFilePicker("Choose archive file", "", ViewHelpers.Archive);
            if (string.IsNullOrEmpty(FilePath))
            {
                return;
            }

            string Extension = Path.GetExtension(FilePath);
            bool IsMap = Extension == ".s6map" || Extension == ".s6xmap";

            bool Result;
            string Message;
            FileInfo Info;

            try
            {
                Info = new(FilePath);
            }
            catch (Exception ex)
            {
                Message = $"An error occured trying to unpack the archive file {Path.GetFileName(FilePath)}.\n{ex.Message}";
                await ShowMessageBox("Unpacking Archive File ...", Message);
                return;
            }

            string ArchiveOutputDirectoryPath = Path.Combine(Info.DirectoryName, Path.GetFileNameWithoutExtension(Info.Name));
            try
            {
                Result = await Archives.ExtractArchiveFileToFolderAsync(FilePath, ArchiveOutputDirectoryPath, IsMap);
            }
            catch (Exception ex)
            {
                Message = $"An error occured trying to unpack the archive file {Path.GetFileName(FilePath)}.\n{ex.Message}";
                await ShowMessageBox("Unpacking Archive File ...", Message);
                return;
            }

            Message = $"Extracted the archive file {Path.GetFileName(FilePath)} to {ArchiveOutputDirectoryPath}.";
            Message += "\n\n" + (Result ? "No errors occured!" : "An error occured!");
            await ShowMessageBox("Unpacking Archive File ...", Message);
        }
    }
}
