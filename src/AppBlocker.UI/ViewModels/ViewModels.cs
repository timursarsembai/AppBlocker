using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AppBlocker.Core.Configuration;
using AppBlocker.Core.Models;
using AppBlocker.UI.IPC;

namespace AppBlocker.UI.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ========================================================================
    //  НАВИГАЦИЯ
    // ========================================================================
    public class MainViewModel : ViewModelBase
    {
        private object _currentViewModel;

        private readonly DashboardViewModel _dashboardVm;
        private readonly BlacklistsViewModel _blacklistsVm;
        private readonly DopamineMenuViewModel _dopamineVm;
        private readonly SettingsViewModel _settingsVm;

        public object CurrentViewModel
        {
            get => _currentViewModel;
            set { _currentViewModel = value; OnPropertyChanged(); }
        }

        public ICommand NavigateDashboardCommand { get; }
        public ICommand NavigateBlacklistsCommand { get; }
        public ICommand NavigateDopamineCommand { get; }
        public ICommand NavigateSettingsCommand { get; }

        public MainViewModel()
        {
            _dashboardVm = new DashboardViewModel();
            _blacklistsVm = new BlacklistsViewModel();
            _dopamineVm = new DopamineMenuViewModel();
            _settingsVm = new SettingsViewModel();

            NavigateDashboardCommand = new RelayCommand(_ => TryNavigate(_dashboardVm));
            NavigateBlacklistsCommand = new RelayCommand(_ => TryNavigate(_blacklistsVm));
            NavigateDopamineCommand = new RelayCommand(_ => TryNavigate(_dopamineVm));
            NavigateSettingsCommand = new RelayCommand(_ => TryNavigate(_settingsVm));

            CurrentViewModel = _dashboardVm;
        }

        private void TryNavigate(object targetViewModel)
        {
            // Если мы уже на этой вкладке, ничего не делаем
            if (CurrentViewModel == targetViewModel)
                return;

            var configManager = new ConfigManager();
            var config = configManager.LoadConfig();

            // Если защита не включена или мы возвращаемся на Дашборд (главный экран обычно безопасен)
            // Но пользователь просил "при переключении в меню", так что защищаем все переходы кроме Дашборда
            if (targetViewModel == _dashboardVm || config.CurrentProtectionType == ProtectionType.None || string.IsNullOrEmpty(config.ProtectionHash))
            {
                CurrentViewModel = targetViewModel;
                return;
            }

            var authVm = new AuthViewModel(config.CurrentProtectionType, config.ProtectionHash, config.RequireMathChallenge);
            
            // Чтобы не нарушать потоки, запускаем в UI-потоке
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var authWin = new AppBlocker.UI.Views.AuthWindow(authVm);
                authWin.Owner = System.Windows.Application.Current.MainWindow;
                if (authWin.ShowDialog() == true)
                {
                    CurrentViewModel = targetViewModel;
                }
            });
        }
    }

    // ========================================================================
    //  1. ДАШБОРД
    // ========================================================================
    public class DashboardViewModel : ViewModelBase
    {
        private readonly ConfigManager _configManager;
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private AppConfig _config;

        private string _statusText = "Ничего не заблокировано";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _statusTimer = "";
        public string StatusTimer
        {
            get => _statusTimer;
            set { _statusTimer = value; OnPropertyChanged(); }
        }

        private string _statusColor = "#A1A1AA";
        public string StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        private bool _isSessionActive;
        public bool IsSessionActive
        {
            get => _isSessionActive;
            set { _isSessionActive = value; OnPropertyChanged(); }
        }

        private int _inputHours = 1;
        public int InputHours
        {
            get => _inputHours;
            set { _inputHours = value; OnPropertyChanged(); }
        }

        private int _inputMinutes = 0;
        public int InputMinutes
        {
            get => _inputMinutes;
            set { _inputMinutes = value; OnPropertyChanged(); }
        }

        public ICommand StartCustomSessionCommand { get; }
        public ICommand StartQuickSessionCommand { get; }
        public ICommand StopSessionCommand { get; }

        public DashboardViewModel()
        {
            _configManager = new ConfigManager();
            _config = _configManager.LoadConfig();

            StartCustomSessionCommand = new RelayCommand(_ => 
            {
                int totalMinutes = (InputHours * 60) + InputMinutes;
                if (totalMinutes > 0)
                {
                    // Use ClarityWindow as the neutral default mode for custom sessions
                    TryStartSession(BlockingMode.ClarityWindow, totalMinutes);
                }
            });

            StartQuickSessionCommand = new RelayCommand(param => 
            {
                if (param is string minStr && int.TryParse(minStr, out int minutes))
                {
                    InputHours = minutes / 60;
                    InputMinutes = minutes % 60;
                }
            });

            StopSessionCommand = new RelayCommand(_ => TryStopSession());

            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) => UpdateStatus();

            UpdateStatus();
        }

        private void TryStartSession(BlockingMode mode, int durationMinutes)
        {
            _config = _configManager.LoadConfig();

            // Если сессия уже активна и режим меняется (перезаписывается),
            // то нужно проверить защиту, так как это равносильно прерыванию текущей сессии
            if (IsSessionActive && _config.CurrentProtectionType != ProtectionType.None && !string.IsNullOrEmpty(_config.ProtectionHash))
            {
                var authVm = new AuthViewModel(_config.CurrentProtectionType, _config.ProtectionHash, _config.RequireMathChallenge);
                
                bool? authResult = false;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var authWin = new AppBlocker.UI.Views.AuthWindow(authVm);
                    authWin.Owner = System.Windows.Application.Current.MainWindow;
                    authResult = authWin.ShowDialog();
                });

                if (authResult != true)
                {
                    return; // Отмена
                }
            }

            StartSession(mode, durationMinutes);
        }

        private void StartSession(BlockingMode mode, int durationMinutes)
        {
            _config = _configManager.LoadConfig();
            _config.CurrentMode = mode;
            _config.BlockEndTime = DateTime.UtcNow.AddMinutes(durationMinutes);
            _configManager.SaveConfig(_config);
            _timer.Start();
            UpdateStatus();
        }

        private void TryStopSession()
        {
            _config = _configManager.LoadConfig();

            // Проверяем, нужна ли авторизация
            if (_config.CurrentProtectionType != ProtectionType.None && !string.IsNullOrEmpty(_config.ProtectionHash))
            {
                var authVm = new AuthViewModel(_config.CurrentProtectionType, _config.ProtectionHash, _config.RequireMathChallenge);
                var authWin = new AppBlocker.UI.Views.AuthWindow(authVm);
                
                if (authWin.ShowDialog() != true)
                {
                    return; // Пользователь отменил или не прошел авторизацию
                }
            }

            StopSession();
        }

        private void StopSession()
        {
            _config = _configManager.LoadConfig();
            _config.CurrentMode = BlockingMode.None;
            _config.BlockEndTime = null;
            _configManager.SaveConfig(_config);
            _timer.Stop();
            IsSessionActive = false;
            StatusText = "Ничего не заблокировано";
            StatusColor = "#A1A1AA";
        }

        private void UpdateStatus()
        {
            _config = _configManager.LoadConfig();
            if (_config.CurrentMode == BlockingMode.None || _config.BlockEndTime == null)
            {
                IsSessionActive = false;
                StatusText = "Ничего не заблокировано";
                StatusTimer = "";
                StatusColor = "#A1A1AA";
                _timer.Stop();
                return;
            }

            var remaining = _config.BlockEndTime.Value.ToUniversalTime() - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0) { StopSession(); return; }

            IsSessionActive = true;
            _timer.Start();

            string modeName = _config.CurrentMode switch
            {
                BlockingMode.ClarityWindow => "Окно ясности",
                BlockingMode.LowerBarrier => "Снижение барьера",
                BlockingMode.Hyperfocus => "Паника",
                _ => "Фокус"
            };

            StatusText = modeName;
            StatusTimer = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            StatusColor = _config.CurrentMode switch
            {
                BlockingMode.Hyperfocus => "#EF4444",
                BlockingMode.LowerBarrier => "#34D399",
                BlockingMode.ClarityWindow => "#60A5FA",
                _ => "#A1A1AA"
            };
        }
    }

    // ========================================================================
    //  2. ЧЕРНЫЕ СПИСКИ
    // ========================================================================
    public class BlockedSite : ViewModelBase
    {
        public string Url { get; set; }
        public string Status { get; set; } = "Заблокирован";
        public string StatusColor { get; set; } = "#F87171";
    }

    public class ProcessEntry : ViewModelBase
    {
        private bool _isBlocked;

        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public bool IsRunning { get; set; } = true;

        public bool IsBlocked
        {
            get => _isBlocked;
            set
            {
                _isBlocked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ActionText));
            }
        }

        public string IndicatorColor => IsRunning ? "#34D399" : "#52525B";
        public string StatusText => IsBlocked ? "Заблокирован" : (IsRunning ? "Активен" : "Не запущен");
        public string StatusColor => IsBlocked ? "#F87171" : (IsRunning ? "#34D399" : "#71717A");
        public string ActionText => IsBlocked ? "Разблокировать" : "Заблокировать";
        public double RowOpacity => IsRunning ? 1.0 : 0.5;
    }

    public class BlacklistsViewModel : ViewModelBase
    {
        private readonly ConfigManager _configManager;
        private AppConfig _currentConfig;

        public ObservableCollection<BlockedSite> BlockedWebsites { get; set; }
        public ObservableCollection<ProcessEntry> BlockedProcesses { get; set; }
        public ObservableCollection<ProcessEntry> FilteredProcesses { get; set; }

        private List<ProcessEntry> _allProcesses = new List<ProcessEntry>();

        private string _newWebsiteText;
        public string NewWebsiteText
        {
            get => _newWebsiteText;
            set { _newWebsiteText = value; OnPropertyChanged(); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); ApplyFilter(); }
        }

        private int _selectedTab;
        public int SelectedTab
        {
            get => _selectedTab;
            set { _selectedTab = value; OnPropertyChanged(); }
        }

        public ICommand AddWebsiteCommand { get; }
        public ICommand RemoveWebsiteCommand { get; }
        public ICommand ToggleBlockProcessCommand { get; }
        public ICommand UnblockProcessCommand { get; }
        public ICommand RefreshProcessesCommand { get; }

        public BlacklistsViewModel()
        {
            _configManager = new ConfigManager();
            _currentConfig = _configManager.LoadConfig();

            BlockedWebsites = new ObservableCollection<BlockedSite>(
                (_currentConfig.BlockedWebsites ?? new List<string>())
                    .Select(url => new BlockedSite { Url = url })
            );

            BlockedProcesses = new ObservableCollection<ProcessEntry>();
            FilteredProcesses = new ObservableCollection<ProcessEntry>();
            LoadRunningProcesses();

            AddWebsiteCommand = new RelayCommand(_ =>
            {
                if (!string.IsNullOrWhiteSpace(NewWebsiteText))
                {
                    var site = NewWebsiteText.Trim().ToLower()
                        .Replace("https://", "").Replace("http://", "").TrimEnd('/');
                    if (!BlockedWebsites.Any(b => b.Url == site))
                    {
                        BlockedWebsites.Add(new BlockedSite { Url = site });
                        SaveWebsites();
                    }
                    NewWebsiteText = string.Empty;
                }
            });

            RemoveWebsiteCommand = new RelayCommand(param =>
            {
                if (param is BlockedSite site) { BlockedWebsites.Remove(site); SaveWebsites(); }
            });

            ToggleBlockProcessCommand = new RelayCommand(param =>
            {
                if (param is ProcessEntry entry) { entry.IsBlocked = !entry.IsBlocked; SaveProcesses(); RebuildLists(); }
            });

            UnblockProcessCommand = new RelayCommand(param =>
            {
                if (param is ProcessEntry entry) { entry.IsBlocked = false; SaveProcesses(); RebuildLists(); }
            });

            RefreshProcessesCommand = new RelayCommand(_ => LoadRunningProcesses());
        }

        private void LoadRunningProcesses()
        {
            _allProcesses.Clear();

            var systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "svchost", "csrss", "lsass", "services", "smss", "wininit",
                "system", "idle", "dwm", "conhost", "sihost", "fontdrvhost",
                "winlogon", "taskhostw", "runtimebroker", "shellexperiencehost",
                "searchhost", "startmenuexperiencehost", "textinputhost",
                "ctfmon", "dllhost", "msdtc", "wudfhost", "audiodg",
                "registry", "memory compression", "secure system",
                "appblocker.service", "appblocker.watchdog", "appblocker.ui",
                "explorer", "ntoskrnl", "msmpeng", "securityhealthservice",
                "spoolsv", "lsaiso", "wlanext", "dashost"
            };

            _currentConfig = _configManager.LoadConfig();
            var blockedSet = new HashSet<string>(
                _currentConfig.BlockedProcesses ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName) && !systemProcesses.Contains(p.ProcessName))
                    .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var fileName = g.Key.ToLower() + ".exe";
                        string displayName = g.Key;
                        try
                        {
                            var firstProc = g.First();
                            var mainModule = firstProc.MainModule;
                            if (mainModule != null)
                            {
                                var fvi = FileVersionInfo.GetVersionInfo(mainModule.FileName);
                                if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                                    displayName = fvi.FileDescription;
                            }
                        }
                        catch { }
                        runningNames.Add(fileName);
                        return new ProcessEntry
                        {
                            DisplayName = displayName,
                            FileName = fileName,
                            IsBlocked = blockedSet.Contains(fileName),
                            IsRunning = true
                        };
                    })
                    .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _allProcesses.AddRange(processes);
            }
            catch { }

            // Добавляем заблокированные, которые НЕ запущены
            foreach (var blockedFile in blockedSet)
            {
                if (!runningNames.Contains(blockedFile))
                {
                    _allProcesses.Add(new ProcessEntry
                    {
                        DisplayName = blockedFile.Replace(".exe", ""),
                        FileName = blockedFile,
                        IsBlocked = true,
                        IsRunning = false
                    });
                }
            }

            RebuildLists();
        }

        private void RebuildLists()
        {
            BlockedProcesses.Clear();
            foreach (var p in _allProcesses.Where(p => p.IsBlocked))
                BlockedProcesses.Add(p);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredProcesses.Clear();
            var source = _allProcesses.Where(p => !p.IsBlocked && p.IsRunning);

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim().ToLower();
                source = source.Where(p =>
                    p.DisplayName.ToLower().Contains(q) ||
                    p.FileName.ToLower().Contains(q));
            }

            foreach (var p in source.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
                FilteredProcesses.Add(p);
        }

        private void SaveWebsites()
        {
            _currentConfig.BlockedWebsites = BlockedWebsites.Select(b => b.Url).ToList();
            _configManager.SaveConfig(_currentConfig);
        }

        private void SaveProcesses()
        {
            _currentConfig.BlockedProcesses = _allProcesses.Where(p => p.IsBlocked).Select(p => p.FileName).ToList();
            _configManager.SaveConfig(_currentConfig);
        }
    }

    // ========================================================================
    //  3. ДОФАМИНОВОЕ МЕНЮ
    // ========================================================================
    public class DopamineHabit : ViewModelBase
    {
        private string _title;
        private bool _isCompleted;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }
    }

    public class DopamineMenuViewModel : ViewModelBase
    {
        private readonly ConfigManager _configManager;

        public ObservableCollection<DopamineHabit> Habits { get; set; }

        private string _newHabitText;
        public string NewHabitText
        {
            get => _newHabitText;
            set { _newHabitText = value; OnPropertyChanged(); }
        }

        public ICommand AddHabitCommand { get; }
        public ICommand RemoveHabitCommand { get; }

        public DopamineMenuViewModel()
        {
            _configManager = new ConfigManager();
            var config = _configManager.LoadConfig();

            if (config.DopamineHabits != null && config.DopamineHabits.Count > 0)
            {
                Habits = new ObservableCollection<DopamineHabit>(
                    config.DopamineHabits.Select(title => new DopamineHabit { Title = title })
                );
            }
            else
            {
                Habits = new ObservableCollection<DopamineHabit>
                {
                    new DopamineHabit { Title = "Выпить стакан воды" },
                    new DopamineHabit { Title = "Сделать 10 приседаний" },
                    new DopamineHabit { Title = "Посмотреть в окно вдаль" },
                    new DopamineHabit { Title = "Сделать 10 глубоких вдохов" },
                    new DopamineHabit { Title = "Пройтись по комнате 2 минуты" },
                    new DopamineHabit { Title = "Размять шею и плечи" },
                };
                SaveHabits();
            }

            AddHabitCommand = new RelayCommand(_ =>
            {
                if (!string.IsNullOrWhiteSpace(NewHabitText))
                {
                    Habits.Add(new DopamineHabit { Title = NewHabitText.Trim() });
                    NewHabitText = string.Empty;
                    SaveHabits();
                }
            });

            RemoveHabitCommand = new RelayCommand(habit =>
            {
                if (habit is DopamineHabit h) { Habits.Remove(h); SaveHabits(); }
            });
        }

        private void SaveHabits()
        {
            var config = _configManager.LoadConfig();
            config.DopamineHabits = Habits.Select(h => h.Title).ToList();
            _configManager.SaveConfig(config);
        }
    }

    // ========================================================================
    //  4. НАСТРОЙКИ
    // ========================================================================
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ConfigManager _configManager;
        private AppConfig _config;

        private bool _autoStartUI;
        public bool AutoStartUI
        {
            get => _autoStartUI;
            set
            {
                _autoStartUI = value;
                OnPropertyChanged();
                _config.AutoStartUI = value;
                UpdateAutoStart(value);
                Save();
            }
        }

        private bool _minimizeToTray;
        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set { _minimizeToTray = value; OnPropertyChanged(); _config.MinimizeToTray = value; Save(); }
        }

        private int _clarityMinutes;
        public int ClarityMinutes
        {
            get => _clarityMinutes;
            set { _clarityMinutes = value; OnPropertyChanged(); _config.ClarityWindowMinutes = value; Save(); }
        }

        private int _lowerBarrierMinutes;
        public int LowerBarrierMinutes
        {
            get => _lowerBarrierMinutes;
            set { _lowerBarrierMinutes = value; OnPropertyChanged(); _config.LowerBarrierMinutes = value; Save(); }
        }

        private int _panicMinutes;
        public int PanicMinutes
        {
            get => _panicMinutes;
            set { _panicMinutes = value; OnPropertyChanged(); _config.PanicMinutes = value; Save(); }
        }

        private ProtectionType _currentProtectionType;
        public ProtectionType CurrentProtectionType
        {
            get => _currentProtectionType;
            set 
            { 
                _currentProtectionType = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsProtectionNone));
                OnPropertyChanged(nameof(IsProtectionPin));
                OnPropertyChanged(nameof(IsProtectionPassword));
                _config.CurrentProtectionType = value; 
                Save(); 
            }
        }

        public bool IsProtectionNone
        {
            get => CurrentProtectionType == ProtectionType.None;
            set { if (value) CurrentProtectionType = ProtectionType.None; }
        }

        public bool IsProtectionPin
        {
            get => CurrentProtectionType == ProtectionType.Pin;
            set { if (value) CurrentProtectionType = ProtectionType.Pin; }
        }

        public bool IsProtectionPassword
        {
            get => CurrentProtectionType == ProtectionType.Password;
            set { if (value) CurrentProtectionType = ProtectionType.Password; }
        }

        private bool _requireMathChallenge;
        public bool RequireMathChallenge
        {
            get => _requireMathChallenge;
            set { _requireMathChallenge = value; OnPropertyChanged(); _config.RequireMathChallenge = value; Save(); }
        }

        public ICommand ResetSettingsCommand { get; }
        public ICommand SetProtectionHashCommand { get; }

        public SettingsViewModel()
        {
            _configManager = new ConfigManager();
            _config = _configManager.LoadConfig();

            _autoStartUI = _config.AutoStartUI;
            _minimizeToTray = _config.MinimizeToTray;
            _clarityMinutes = _config.ClarityWindowMinutes;
            _lowerBarrierMinutes = _config.LowerBarrierMinutes;
            _panicMinutes = _config.PanicMinutes;
            _currentProtectionType = _config.CurrentProtectionType;
            _requireMathChallenge = _config.RequireMathChallenge;

            ResetSettingsCommand = new RelayCommand(_ =>
            {
                ClarityMinutes = 120;
                LowerBarrierMinutes = 60;
                PanicMinutes = 60;
                AutoStartUI = true;
                MinimizeToTray = true;
            });

            SetProtectionHashCommand = new RelayCommand(param =>
            {
                if (param is string rawPassword)
                {
                    _config.ProtectionHash = AppBlocker.Core.Helpers.CryptoHelper.ComputeSha256Hash(rawPassword);
                    Save();
                }
            });
        }

        private void Save()
        {
            _configManager.SaveConfig(_config);
        }

        private void UpdateAutoStart(bool enable)
        {
            try
            {
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        var exePath = System.Reflection.Assembly.GetEntryAssembly()?.Location?.Replace(".dll", ".exe");
                        if (!string.IsNullOrEmpty(exePath))
                            key.SetValue("AppBlocker", $"\"{exePath}\" -minimized");
                    }
                    else
                    {
                        key.DeleteValue("AppBlocker", false);
                    }
                    key.Close();
                }
            }
            catch { }
        }
    }

    // ========================================================================
    //  5. АУТЕНТИФИКАЦИЯ (АВТОРИЗАЦИЯ ОСТАНОВКИ)
    // ========================================================================
    public class AuthViewModel : ViewModelBase
    {
        private readonly string _correctHash;
        private readonly bool _requireMath;
        private readonly Random _rand = new Random();

        public Action OnSuccess { get; set; }
        public Action OnCancel { get; set; }

        private string _promptText;
        public string PromptText
        {
            get => _promptText;
            set { _promptText = value; OnPropertyChanged(); }
        }

        private bool _isMathStage;
        public bool IsMathStage
        {
            get => _isMathStage;
            set { _isMathStage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAuthStage)); }
        }

        public bool IsAuthStage => !IsMathStage;

        private string _mathProblem;
        public string MathProblem
        {
            get => _mathProblem;
            set { _mathProblem = value; OnPropertyChanged(); }
        }

        private int _correctMathAnswer;

        private string _mathAnswerInput;
        public string MathAnswerInput
        {
            get => _mathAnswerInput;
            set { _mathAnswerInput = value; OnPropertyChanged(); }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ICommand SubmitCommand { get; }
        public ICommand CancelCommand { get; }

        public AuthViewModel(ProtectionType type, string correctHash, bool requireMath)
        {
            _correctHash = correctHash;
            _requireMath = requireMath;
            IsMathStage = false;

            PromptText = type == ProtectionType.Pin ? "Введите PIN-код для подтверждения:" : "Введите пароль для подтверждения:";

            SubmitCommand = new RelayCommand(param =>
            {
                ErrorMessage = "";
                if (!IsMathStage)
                {
                    // Проверка пароля/пина
                    var input = param as string ?? "";
                    if (AppBlocker.Core.Helpers.CryptoHelper.ComputeSha256Hash(input) == _correctHash)
                    {
                        if (_requireMath)
                        {
                            GenerateMathProblem();
                            IsMathStage = true;
                        }
                        else
                        {
                            OnSuccess?.Invoke();
                        }
                    }
                    else
                    {
                        ErrorMessage = "Неверный " + (type == ProtectionType.Pin ? "PIN-код" : "пароль");
                    }
                }
                else
                {
                    // Проверка математики
                    if (int.TryParse(MathAnswerInput, out int answer) && answer == _correctMathAnswer)
                    {
                        OnSuccess?.Invoke();
                    }
                    else
                    {
                        ErrorMessage = "Неверный ответ. Попробуйте еще раз.";
                        MathAnswerInput = "";
                        GenerateMathProblem(); // Генерируем новый пример
                    }
                }
            });

            CancelCommand = new RelayCommand(_ => OnCancel?.Invoke());
        }

        private void GenerateMathProblem()
        {
            int a = _rand.Next(11, 20); // 11-19
            int b = _rand.Next(5, 10);  // 5-9
            int c = _rand.Next(10, 50); // 10-49

            _correctMathAnswer = a * b + c;
            MathProblem = $"{a} × {b} + {c} = ?";
        }
    }
}
