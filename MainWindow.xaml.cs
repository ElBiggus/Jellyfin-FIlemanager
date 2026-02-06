using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FMove
{
    public partial class MainWindow : Window
    {
        private string? targetFolder;
        private string? sourceFolder;
        private ObservableCollection<VideoFileInfo> videoFiles = new ObservableCollection<VideoFileInfo>();
        private readonly string[] videoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".mpg", ".mpeg" };
        private bool sameDrive = false;
        private bool moviesOperationCancelled = false;
        private ObservableCollection<MovieFolderInfo> movieFolders = new ObservableCollection<MovieFolderInfo>();

        public MainWindow()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = videoFiles;
            MoviesResultsListBox.ItemsSource = movieFolders;
        }

        private void TargetBorder_Drop(object sender, DragEventArgs e)
        {
            ResetBorderStyle(TargetBorder);
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Directory.Exists(files[0]))
                {
                    targetFolder = files[0];
                    TargetText.Text = targetFolder;
                    TargetText.Foreground = new SolidColorBrush(Colors.Black);
                    
                    // Auto-fill Show Name from target folder name
                    try
                    {
                        string folderName = Path.GetFileName(targetFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!string.IsNullOrEmpty(folderName))
                        {
                            ShowNameTextBox.Text = folderName;
                        }
                    }
                    catch { }
                    
                    UpdateSearchButtonState();
                }
            }
        }

        private void SourceBorder_Drop(object sender, DragEventArgs e)
        {
            ResetBorderStyle(SourceBorder);
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Directory.Exists(files[0]))
                {
                    sourceFolder = files[0];
                    SourceText.Text = sourceFolder;
                    SourceText.Foreground = new SolidColorBrush(Colors.Black);
                    UpdateSearchButtonState();
                }
            }
        }

        private void MoviesTargetBorder_Drop(object sender, DragEventArgs e)
        {
            ResetBorderStyle(MoviesTargetBorder);
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Directory.Exists(files[0]))
                {
                    MoviesTargetText.Text = files[0];
                    MoviesTargetText.Foreground = new SolidColorBrush(Colors.Black);
                    UpdateMoviesGoButtonState();
                }
            }
        }

        private void MoviesSourceBorder_Drop(object sender, DragEventArgs e)
        {
            ResetBorderStyle(MoviesSourceBorder);
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Directory.Exists(files[0]))
                {
                    MoviesSourceText.Text = files[0];
                    MoviesSourceText.Foreground = new SolidColorBrush(Colors.Black);
                    UpdateMoviesGoButtonState();
                }
            }
        }

        private async void MoviesGoButton_Click(object sender, RoutedEventArgs e)
        {
            string moviesTargetFolder = MoviesTargetText.Text;
            string moviesSourceFolder = MoviesSourceText.Text;
            
            if (string.IsNullOrEmpty(moviesTargetFolder) || moviesTargetFolder == "Drag target folder here..." ||
                string.IsNullOrEmpty(moviesSourceFolder) || moviesSourceFolder == "Drag source folder here...")
            {
                MessageBox.Show("Please specify both target and source folders", "Missing Information", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if currently running - if so, this is a STOP request
            if (MoviesGoButton.Content.ToString() == "STOP!")
            {
                moviesOperationCancelled = true;
                MoviesGoButton.IsEnabled = false;
                MoviesProgressText.Text = "Stopping after current operation...";
                return;
            }

            // Start the operation
            moviesOperationCancelled = false;
            MoviesGoButton.Content = "STOP!";
            MoviesProgressPanel.Visibility = Visibility.Visible;
            MoviesCopyRadioButton.IsEnabled = false;
            MoviesMoveRadioButton.IsEnabled = false;

            bool isMoveOperation = MoviesMoveRadioButton.IsChecked == true;
            string operationText = isMoveOperation ? "Moving" : "Copying";

            try
            {
                // Step 0: Count all files for progress tracking
                MoviesProgressText.Text = "Scanning for files...";
                MoviesProgressBar.Value = 0;
                await System.Threading.Tasks.Task.Delay(10);

                int totalFilesCount = await System.Threading.Tasks.Task.Run(() => 
                    CountFilesRecursively(moviesSourceFolder));

                if (moviesOperationCancelled)
                {
                    ResetMoviesUI();
                    return;
                }

                int filesProcessed = 0;

                // Steps 1-4: Process subfolders containing videos
                var subFolders = Directory.GetDirectories(moviesSourceFolder, "*", SearchOption.TopDirectoryOnly);
                var processedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var foldersWithoutVideos = new List<string>();

                foreach (var folder in subFolders)
                {
                    if (moviesOperationCancelled) break;

                    string folderName = Path.GetFileName(folder);
                    MoviesProgressText.Text = $"Processing: {folderName}";

                    // Step 1: Search for video files in this folder tree
                    var videoFiles = await System.Threading.Tasks.Task.Run(() => 
                        Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                            .ToList());

                    if (videoFiles.Count > 0)
                    {
                        // Step 2: Find the largest video file
                        var largestVideo = videoFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                        string targetMovieFolder = Path.Combine(moviesTargetFolder, folderName);
                        Directory.CreateDirectory(targetMovieFolder);

                        // Move/copy the largest video file
                        string videoFileName = Path.GetFileName(largestVideo);
                        string videoDestination = Path.Combine(targetMovieFolder, videoFileName);
                        
                        if (await ProcessFile(largestVideo, videoDestination, isMoveOperation, filesProcessed, totalFilesCount, operationText))
                            filesProcessed++;
                        if (moviesOperationCancelled) break;

                        // Check for matching .srt file
                        string srtPath = Path.ChangeExtension(largestVideo, ".srt");
                        if (File.Exists(srtPath))
                        {
                            string srtDestination = Path.Combine(targetMovieFolder, Path.GetFileName(srtPath));
                            if (await ProcessFile(srtPath, srtDestination, isMoveOperation, filesProcessed, totalFilesCount, operationText))
                                filesProcessed++;
                            if (moviesOperationCancelled) break;
                        }

                        // Step 3: Move/copy all other content to Extra subfolder
                        string extraFolder = Path.Combine(targetMovieFolder, "Extra");
                        filesProcessed += await ProcessRemainingContent(folder, extraFolder, largestVideo, srtPath, isMoveOperation, filesProcessed, totalFilesCount, operationText);
                        
                        processedFolders.Add(folder);
                    }
                    else
                    {
                        foldersWithoutVideos.Add(folder);
                    }
                }

                if (moviesOperationCancelled)
                {
                    ResetMoviesUI();
                    return;
                }

                // Step 5: Process video files directly in source root
                var rootVideos = Directory.GetFiles(moviesSourceFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();

                foreach (var videoFile in rootVideos)
                {
                    if (moviesOperationCancelled) break;

                    string videoFileName = Path.GetFileNameWithoutExtension(videoFile);
                    string extension = Path.GetExtension(videoFile);
                    string targetMovieFolder = Path.Combine(moviesTargetFolder, videoFileName);
                    Directory.CreateDirectory(targetMovieFolder);

                    string videoDestination = Path.Combine(targetMovieFolder, Path.GetFileName(videoFile));
                    if (await ProcessFile(videoFile, videoDestination, isMoveOperation, filesProcessed, totalFilesCount, operationText))
                        filesProcessed++;
                    if (moviesOperationCancelled) break;

                    // Check for matching .srt file
                    string srtPath = Path.ChangeExtension(videoFile, ".srt");
                    if (File.Exists(srtPath))
                    {
                        string srtDestination = Path.Combine(targetMovieFolder, Path.GetFileName(srtPath));
                        if (await ProcessFile(srtPath, srtDestination, isMoveOperation, filesProcessed, totalFilesCount, operationText))
                            filesProcessed++;
                        if (moviesOperationCancelled) break;
                    }
                }

                if (moviesOperationCancelled)
                {
                    ResetMoviesUI();
                    return;
                }

                // Move/copy remaining files and folders without videos to TEMP
                string tempFolder = Path.Combine(moviesTargetFolder, "TEMP");
                
                // Process folders without videos
                foreach (var folder in foldersWithoutVideos)
                {
                    if (moviesOperationCancelled) break;
                    
                    string folderName = Path.GetFileName(folder);
                    string tempDestination = Path.Combine(tempFolder, folderName);
                    filesProcessed += await ProcessDirectory(folder, tempDestination, isMoveOperation, filesProcessed, totalFilesCount, operationText);
                }

                // Process remaining root files (non-video files)
                var rootFiles = Directory.GetFiles(moviesSourceFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => !videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();

                foreach (var file in rootFiles)
                {
                    if (moviesOperationCancelled) break;
                    
                    Directory.CreateDirectory(tempFolder);
                    string fileName = Path.GetFileName(file);
                    string tempDestination = Path.Combine(tempFolder, fileName);
                    if (await ProcessFile(file, tempDestination, isMoveOperation, filesProcessed, totalFilesCount, operationText))
                        filesProcessed++;
                }

                if (!moviesOperationCancelled)
                {
                    // Step 6: Show editable list of movie folders
                    await PopulateMovieFoldersList(moviesTargetFolder);
                    
                    MessageBox.Show($"Operation completed successfully!\nProcessed {filesProcessed} files.\n\nYou can now edit movie folder names below.", 
                        "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Operation stopped by user.\nProcessed {filesProcessed} files before stopping.", 
                        "Stopped", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during operation: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ResetMoviesUI();
            }
        }

        private int CountFilesRecursively(string directory)
        {
            try
            {
                int count = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly).Length;
                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    count += CountFilesRecursively(subDir);
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private async System.Threading.Tasks.Task<bool> ProcessFile(string sourcePath, string destinationPath, bool isMoveOperation, int filesProcessed, int totalFiles, string operationText)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                
                if (isMoveOperation)
                {
                    File.Move(sourcePath, destinationPath, true);
                }
                else
                {
                    File.Copy(sourcePath, destinationPath, true);
                }

                MoviesProgressBar.Value = (double)(filesProcessed + 1) / totalFiles * 100;
                await System.Threading.Tasks.Task.Delay(1);
                return true;
            }
            catch (Exception ex)
            {
                // Log error but continue
                System.Diagnostics.Debug.WriteLine($"Error processing file {sourcePath}: {ex.Message}");
                return false;
            }
        }

        private async System.Threading.Tasks.Task<int> ProcessRemainingContent(string sourceFolder, string extraFolder, string excludeVideo, string? excludeSrt, bool isMoveOperation, int filesProcessed, int totalFiles, string operationText)
        {
            int count = 0;
            try
            {
                var allFiles = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories);
                
                foreach (var file in allFiles)
                {
                    if (moviesOperationCancelled) break;
                    
                    // Skip the main video and its srt
                    if (file.Equals(excludeVideo, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(excludeSrt) && file.Equals(excludeSrt, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // Maintain relative path structure in Extra folder
                    string relativePath = Path.GetRelativePath(sourceFolder, file);
                    string destination = Path.Combine(extraFolder, relativePath);
                    
                    if (await ProcessFile(file, destination, isMoveOperation, filesProcessed + count, totalFiles, operationText))
                    {
                        count++;
                    }
                }

                // If moving, delete empty directories
                if (isMoveOperation)
                {
                    await System.Threading.Tasks.Task.Run(() => DeleteEmptyDirectories(sourceFolder));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing remaining content: {ex.Message}");
            }
            return count;
        }

        private async System.Threading.Tasks.Task<int> ProcessDirectory(string sourceDir, string targetDir, bool isMoveOperation, int filesProcessed, int totalFiles, string operationText)
        {
            int count = 0;
            try
            {
                Directory.CreateDirectory(targetDir);

                // Copy all files
                foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    if (moviesOperationCancelled) break;
                    
                    string relativePath = Path.GetRelativePath(sourceDir, file);
                    string destination = Path.Combine(targetDir, relativePath);
                    if (await ProcessFile(file, destination, isMoveOperation, filesProcessed + count, totalFiles, operationText))
                    {
                        count++;
                    }
                }

                // If moving, delete the source directory
                if (isMoveOperation && !moviesOperationCancelled)
                {
                    await System.Threading.Tasks.Task.Run(() => Directory.Delete(sourceDir, true));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing directory {sourceDir}: {ex.Message}");
            }
            return count;
        }

        private void DeleteEmptyDirectories(string directory)
        {
            try
            {
                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    DeleteEmptyDirectories(subDir);
                }

                if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
                {
                    Directory.Delete(directory, false);
                }
            }
            catch
            {
                // Ignore errors when deleting directories
            }
        }

        private void ResetMoviesUI()
        {
            MoviesGoButton.Content = "Go!";
            MoviesGoButton.IsEnabled = true;
            MoviesCopyRadioButton.IsEnabled = true;
            MoviesMoveRadioButton.IsEnabled = true;
            MoviesProgressPanel.Visibility = Visibility.Collapsed;
            MoviesProgressBar.Value = 0;
            moviesOperationCancelled = false;
        }

        private async System.Threading.Tasks.Task PopulateMovieFoldersList(string targetFolder)
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                Dispatcher.Invoke(() => movieFolders.Clear());

                var directories = Directory.GetDirectories(targetFolder)
                    .Where(d => !Path.GetFileName(d).Equals("TEMP", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => Path.GetFileName(d));

                foreach (var dir in directories)
                {
                    string folderName = Path.GetFileName(dir);
                    Dispatcher.Invoke(() =>
                    {
                        movieFolders.Add(new MovieFolderInfo
                        {
                            DisplayName = folderName,
                            OriginalName = folderName,
                            FolderPath = dir
                        });
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    if (movieFolders.Count > 0)
                    {
                        MoviesResultsGroup.Visibility = Visibility.Visible;
                    }
                });
            });
        }

        private void MovieFolderTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Tab)
            {
                var textBox = sender as TextBox;
                if (textBox?.Tag is MovieFolderInfo folderInfo)
                {
                    RenameMovieFolder(folderInfo);
                }
            }
        }

        private void MovieFolderTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox?.Tag is MovieFolderInfo folderInfo)
            {
                RenameMovieFolder(folderInfo);
            }
        }

        private void RenameMovieFolder(MovieFolderInfo folderInfo)
        {
            string newName = folderInfo.DisplayName.Trim();
            
            // Validate new name
            if (string.IsNullOrEmpty(newName) || newName == folderInfo.OriginalName)
            {
                folderInfo.DisplayName = folderInfo.OriginalName;
                return;
            }

            // Check for invalid characters
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("Folder name contains invalid characters.", "Invalid Name", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                folderInfo.DisplayName = folderInfo.OriginalName;
                return;
            }

            try
            {
                string parentDir = Path.GetDirectoryName(folderInfo.FolderPath)!;
                string newFolderPath = Path.Combine(parentDir, newName);

                // Check if folder already exists
                if (Directory.Exists(newFolderPath) && !newFolderPath.Equals(folderInfo.FolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("A folder with this name already exists.", "Name Conflict", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    folderInfo.DisplayName = folderInfo.OriginalName;
                    return;
                }

                // Rename video files and .srt files
                var videoFiles = Directory.GetFiles(folderInfo.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();

                foreach (var videoFile in videoFiles)
                {
                    string extension = Path.GetExtension(videoFile);
                    string newVideoFileName = newName + extension;
                    string newVideoPath = Path.Combine(folderInfo.FolderPath, newVideoFileName);
                    
                    if (!videoFile.Equals(newVideoPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(videoFile, newVideoPath);
                    }

                    // Rename matching .srt file
                    string oldSrtPath = Path.ChangeExtension(videoFile, ".srt");
                    if (File.Exists(oldSrtPath))
                    {
                        string newSrtFileName = newName + ".srt";
                        string newSrtPath = Path.Combine(folderInfo.FolderPath, newSrtFileName);
                        
                        if (!oldSrtPath.Equals(newSrtPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Move(oldSrtPath, newSrtPath);
                        }
                    }
                }

                // Rename the folder
                if (!folderInfo.FolderPath.Equals(newFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Move(folderInfo.FolderPath, newFolderPath);
                }

                // Update the folder info
                folderInfo.FolderPath = newFolderPath;
                folderInfo.OriginalName = newName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error renaming folder: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                folderInfo.DisplayName = folderInfo.OriginalName;
            }
        }

        private void UpdateMoviesGoButtonState()
        {
            MoviesGoButton.IsEnabled = !string.IsNullOrEmpty(MoviesTargetText.Text) && 
                                       MoviesTargetText.Text != "Drag target folder here..." &&
                                       !string.IsNullOrEmpty(MoviesSourceText.Text) && 
                                       MoviesSourceText.Text != "Drag source folder here...";
        }

        private void Border_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                Border border = (Border)sender;
                border.BorderBrush = new SolidColorBrush(Colors.DodgerBlue);
                border.Background = new SolidColorBrush(Color.FromRgb(230, 240, 255));
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Border_DragLeave(object sender, DragEventArgs e)
        {
            ResetBorderStyle((Border)sender);
        }

        private void ResetBorderStyle(Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(204, 204, 204));
            border.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        }

        private void UpdateSearchButtonState()
        {
            SearchButton.IsEnabled = !string.IsNullOrEmpty(targetFolder) && !string.IsNullOrEmpty(sourceFolder);
            
            // Check if source and target are on the same drive
            if (!string.IsNullOrEmpty(targetFolder) && !string.IsNullOrEmpty(sourceFolder))
            {
                try
                {
                    string targetRoot = Path.GetPathRoot(targetFolder) ?? string.Empty;
                    string sourceRoot = Path.GetPathRoot(sourceFolder) ?? string.Empty;
                    sameDrive = string.Equals(targetRoot, sourceRoot, StringComparison.OrdinalIgnoreCase);
                    MoveRadioButton.IsEnabled = sameDrive;
                    
                    if (!sameDrive && MoveRadioButton.IsChecked == true)
                    {
                        CopyRadioButton.IsChecked = true;
                    }
                }
                catch
                {
                    sameDrive = false;
                    MoveRadioButton.IsEnabled = false;
                }
            }
            else
            {
                MoveRadioButton.IsEnabled = false;
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(sourceFolder))
                return;

            videoFiles.Clear();

            try
            {
                var files = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();

                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var (season, episode) = ParseSeasonEpisode(fileName);
                    
                    videoFiles.Add(new VideoFileInfo
                    {
                        Season = season,
                        Episode = episode,
                        Filename = Path.GetFileName(file),
                        FullPath = file
                    });
                }

                // Sort by season then episode
                var sortedFiles = videoFiles.OrderBy(v =>
                {
                    if (int.TryParse(v.Season, out int s))
                        return s;
                    return int.MaxValue;
                }).ThenBy(v =>
                {
                    if (int.TryParse(v.Episode, out int e))
                        return e;
                    return int.MaxValue;
                }).ToList();

                videoFiles.Clear();
                foreach (var item in sortedFiles)
                {
                    videoFiles.Add(item);
                }

                GoButton.IsEnabled = videoFiles.Count > 0;

                MessageBox.Show($"Found {videoFiles.Count} video file(s)", "Search Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error searching files: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private (string season, string episode) ParseSeasonEpisode(string filename)
        {
            // Pattern 1: SxxEyy or SxEy
            var match = Regex.Match(filename, @"[Ss](\d{1,2})[Ee](\d{1,2})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return (match.Groups[1].Value, match.Groups[2].Value);
            }

            // Pattern 2: SxE format (e.g., 3x7 for S03E07)
            match = Regex.Match(filename, @"(\d{1,2})[xX](\d{1,2})");
            if (match.Success)
            {
                return (match.Groups[1].Value, match.Groups[2].Value);
            }

            // Pattern 3: Season xx Episode yy
            match = Regex.Match(filename, @"Season\s*(\d{1,2})\s*Episode\s*(\d{1,2})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return (match.Groups[1].Value, match.Groups[2].Value);
            }

            // Pattern 4: xxEyy (e.g., 301 for S03E01)
            match = Regex.Match(filename, @"(\d)(\d{2})[Ee](\d{1,2})");
            if (match.Success)
            {
                return (match.Groups[1].Value, match.Groups[3].Value);
            }

            // Pattern 5: xxyy format (e.g., 0301 for S03E01)
            match = Regex.Match(filename, @"(\d{1,2})(\d{2})");
            if (match.Success && match.Groups[1].Value.Length <= 2)
            {
                int possibleSeason = int.Parse(match.Groups[1].Value);
                int possibleEpisode = int.Parse(match.Groups[2].Value);
                
                // Only accept if season is reasonable (1-99) and episode is reasonable (1-99)
                if (possibleSeason >= 1 && possibleSeason <= 99 && possibleEpisode >= 1 && possibleEpisode <= 99)
                {
                    return (match.Groups[1].Value, match.Groups[2].Value);
                }
            }

            return (string.Empty, string.Empty);
        }

        private void ProcessSubtitleFile(string videoFilePath, string seasonFolder, string showName, string seasonPadded, string episodePadded, bool isMoveOperation)
        {
            try
            {
                // First check for .srt file in the same location
                string srtPath = Path.ChangeExtension(videoFilePath, ".srt");
                
                // If not found in same location, search recursively in source folder for matching season/episode
                if (!File.Exists(srtPath) && !string.IsNullOrEmpty(sourceFolder))
                {
                    var allSrtFiles = Directory.GetFiles(sourceFolder, "*.srt", SearchOption.AllDirectories);
                    
                    foreach (var srtFile in allSrtFiles)
                    {
                        var srtFileName = Path.GetFileNameWithoutExtension(srtFile);
                        var (srtSeason, srtEpisode) = ParseSeasonEpisode(srtFileName);
                        
                        // Check if season and episode match
                        if (srtSeason == seasonPadded.TrimStart('0') && srtEpisode == episodePadded.TrimStart('0'))
                        {
                            srtPath = srtFile;
                            break;
                        }
                    }
                }
                
                if (File.Exists(srtPath))
                {
                    string newSrtFileName = $"{showName} S{seasonPadded}E{episodePadded}.srt";
                    string srtDestinationPath = Path.Combine(seasonFolder, newSrtFileName);
                    
                    if (isMoveOperation)
                    {
                        FileInfo srtFileInfo = new FileInfo(srtPath);
                        srtFileInfo.MoveTo(srtDestinationPath, overwrite: true);
                    }
                    else
                    {
                        File.Copy(srtPath, srtDestinationPath, overwrite: true);
                    }
                }
            }
            catch
            {
                // Silently ignore subtitle file errors - don't interrupt the main operation
            }
        }

        private async void GoButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(targetFolder) || videoFiles.Count == 0)
                return;

            string showName = ShowNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(showName))
            {
                MessageBox.Show("Please enter a show name", "Missing Information", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Disable controls during operation
            GoButton.IsEnabled = false;
            SearchButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            
            bool isMoveOperation = MoveRadioButton.IsChecked == true;
            string operationText = isMoveOperation ? "Moving" : "Copying";

            try
            {
                int successCount = 0;
                int errorCount = 0;
                int totalFiles = videoFiles.Count;
                int currentFile = 0;

                // Process files in reverse order to safely remove items from the collection
                for (int i = videoFiles.Count - 1; i >= 0; i--)
                {
                    var video = videoFiles[i];
                    currentFile++;

                    if (string.IsNullOrEmpty(video.Season) || string.IsNullOrEmpty(video.Episode))
                    {
                        errorCount++;
                        continue;
                    }

                    // Pad season and episode to 2 digits
                    string seasonPadded = int.Parse(video.Season).ToString("D2");
                    string episodePadded = int.Parse(video.Episode).ToString("D2");

                    // Update progress
                    ProgressText.Text = $"{operationText} S{seasonPadded}E{episodePadded} - file {currentFile} of {totalFiles}";
                    CopyProgressBar.Value = (double)currentFile / totalFiles * 100;
                    
                    // Allow UI to update
                    await System.Threading.Tasks.Task.Delay(10);

                    // Create season folder
                    string seasonFolder = Path.Combine(targetFolder, $"Season {seasonPadded}");
                    Directory.CreateDirectory(seasonFolder);

                    // Build new filename
                    string extension = Path.GetExtension(video.FullPath);
                    string newFileName = $"{showName} S{seasonPadded}E{episodePadded}{extension}";
                    string destinationPath = Path.Combine(seasonFolder, newFileName);

                    // Copy or Move file
                    try
                    {
                        if (isMoveOperation)
                        {
                            FileInfo fileInfo = new FileInfo(video.FullPath);
                            fileInfo.MoveTo(destinationPath, overwrite: false);
                        }
                        else
                        {
                            File.Copy(video.FullPath, destinationPath, overwrite: false);
                        }
                        successCount++;
                        
                        // Check for matching .srt file
                        ProcessSubtitleFile(video.FullPath, seasonFolder, showName, seasonPadded, episodePadded, isMoveOperation);
                        
                        // Remove from list after successful operation
                        videoFiles.RemoveAt(i);
                    }
                    catch (IOException)
                    {
                        // File already exists, ask user
                        var result = MessageBox.Show(
                            $"File already exists:\n{destinationPath}\n\nOverwrite?",
                            "File Exists",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            if (isMoveOperation)
                            {
                                FileInfo fileInfo = new FileInfo(video.FullPath);
                                fileInfo.MoveTo(destinationPath, overwrite: true);
                            }
                            else
                            {
                                File.Copy(video.FullPath, destinationPath, overwrite: true);
                            }
                            successCount++;
                            
                            // Check for matching .srt file
                            ProcessSubtitleFile(video.FullPath, seasonFolder, showName, seasonPadded, episodePadded, isMoveOperation);
                            
                            // Remove from list after successful operation
                            videoFiles.RemoveAt(i);
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                }

                string completionVerb = isMoveOperation ? "moved" : "copied";
                MessageBox.Show(
                    $"Operation complete!\n\nSuccessfully {completionVerb}: {successCount}\nSkipped/Failed: {errorCount}",
                    "Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying files: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable controls and hide progress
                ProgressPanel.Visibility = Visibility.Collapsed;
                GoButton.IsEnabled = true;
                SearchButton.IsEnabled = true;
                CopyProgressBar.Value = 0;
                ProgressText.Text = string.Empty;
            }
        }
    }

    public class VideoFileInfo : INotifyPropertyChanged
    {
        private string season = string.Empty;
        private string episode = string.Empty;

        public string Season
        {
            get => season;
            set
            {
                season = value;
                OnPropertyChanged(nameof(Season));
            }
        }

        public string Episode
        {
            get => episode;
            set
            {
                episode = value;
                OnPropertyChanged(nameof(Episode));
            }
        }

        public string Filename { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MovieFolderInfo : INotifyPropertyChanged
    {
        private string displayName = string.Empty;
        private string originalName = string.Empty;

        public string DisplayName
        {
            get => displayName;
            set
            {
                displayName = value;
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string OriginalName
        {
            get => originalName;
            set => originalName = value;
        }

        public string FolderPath { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
