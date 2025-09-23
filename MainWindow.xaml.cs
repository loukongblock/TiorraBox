using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using Ookii.Dialogs.Wpf;
using System.Net.Http.Json;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Newtonsoft.Json;
using System.Threading;
using File = System.IO.File;
using Path = System.IO.Path;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;
using static System.Net.WebRequestMethods;
using System.Text.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;
using System.Net.Security;
using System.Net;
using System.Net.NetworkInformation;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Globalization;
using System.Security.Principal;
using System.Security.Policy;
using System.Runtime.InteropServices.ComTypes;
using static TiorraBox.MainWindow;

namespace TiorraBox
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private HttpClient _httpClient;
        private bool _memoryEnabled = false;
        private readonly List<ChatMessage> _history;
        private List<ChatSession> _sessions;
        private ChatSession _current;
        public static bool IsRunningAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        public static void RestartAsAdmin()
        {
            if (!IsRunningAsAdmin())
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    Verb = "runas",
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1))
                };
                Process.Start(processInfo);
                Environment.Exit(0);
            }
        }
        public class ModelPath
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }
        private readonly string _cfgPath = Path.Combine(
    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
    "config.json");
        private Config _cfg;


        public class Config
        {
            public bool jiyi { get; set; } = true;
            public double wendu { get; set; } = 0.7;
            public int shangxiawen { get; set; } = 5;
            public bool zhedie { get; set; } = false;

            public int keepRounds { get; set; } = 2;   // 默认保留 2 轮
            public double chengfa { get; set; } = 1.1;
            public string renshe { get; set; } = "You are DeepSeek-R1, a reasoning model. Think step by step inside <think> tags. 请使用提问者提问时所用的语言回答";
        }

        public class ChatSession
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public List<ChatMessage> Messages { get; set; }

            public ChatSession()
            {
                Messages = new List<ChatMessage>();
            }
        }

        // 会话仓库
        internal static class ChatSessionStorage
        {
            private static readonly string FilePath =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sessions.json");

            public static List<ChatSession> Load()
            {
                if (!File.Exists(FilePath))
                    return new List<ChatSession>();

                string json = File.ReadAllText(FilePath);
                List<ChatSession> list =
                    JsonConvert.DeserializeObject<List<ChatSession>>(json);
                return list ?? new List<ChatSession>();
            }

            public static void Save(IEnumerable<ChatSession> list)
            {
                string json = JsonConvert.SerializeObject(list, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
        }

        internal static class ModelStorage
        {
            private static readonly string CentralJson =
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modelpath.json");

            public static List<ModelPath> Load()
            {
                if (!File.Exists(CentralJson)) return new List<ModelPath>();
                return JsonConvert.DeserializeObject<List<ModelPath>>(File.ReadAllText(CentralJson))
                       ?? new List<ModelPath>();
            }

            public static void Save(IEnumerable<ModelPath> list)
            {
                File.WriteAllText(CentralJson,
                    JsonConvert.SerializeObject(list, Formatting.Indented));
            }
        }

        private static readonly HttpClient http = new HttpClient();
        private static readonly string HistoryPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_history.json");
        private static readonly HttpClient _http = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
        public enum BackendType
        {
            LlamaCpp,   // http://localhost:8080  /v1/chat/completions
            Ollama      // http://localhost:11434 /api/chat
        }

        private static readonly HttpClient client = new HttpClient();

        private string CurrentModelName = "AI";   // 默认


        private void LoadOrCreateConfig()
        {
            if (!File.Exists(_cfgPath))
            {
                _cfg = new Config();                  
                SaveConfig();                         
            }
            else
            {
                var json = File.ReadAllText(_cfgPath);
                _cfg = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
            }
        }
        private void SaveConfig()
        {
            var json = JsonConvert.SerializeObject(_cfg, Formatting.Indented);
            File.WriteAllText(_cfgPath, json);
        }

        private void LoadConfigToControls()
        {
            if (!File.Exists(_cfgPath))
            {
                _cfg = new Config();   
                SaveConfig();         
            }
            else
            {
                var json = File.ReadAllText(_cfgPath);
                _cfg = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
            }

            renshebianxie.Text = _cfg.renshe;
            if (_cfg.jiyi == false)
            {
                isen.HorizontalAlignment = HorizontalAlignment.Left;
                _memoryEnabled = false;
                openjiyibaocun.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
            }
            else
            {
                openjiyibaocun.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00b96b"));
                isen.HorizontalAlignment = HorizontalAlignment.Right;
                _memoryEnabled = true;
            }
            wendu.Text = _cfg.wendu.ToString(CultureInfo.InvariantCulture);
            chengfa.Text = _cfg.chengfa.ToString(CultureInfo.InvariantCulture);
            lunshu.Text = _cfg.keepRounds.ToString(CultureInfo.InvariantCulture);
        }
        private const string MutexName = "Global\\TiorraBox";



        

        public MainWindow()
        {
            IsRunningAsAdmin();
            RestartAsAdmin();
            InitializeComponent();
            Loaded += OnWindowLoaded;
            LoadOrCreateConfig();
            LoadConfigToControls();
            _sessions = ChatSessionStorage.Load();
            if (_sessions.Count == 0)
            {
                ChatSession def = new ChatSession();
                def.Id = Guid.NewGuid().ToString();
                def.Title = "默认对话";
                _sessions.Add(def);
            }
            //转移chat_history.json
            string oldFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                          "chat_history.json");
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            if (File.Exists(oldFile))
            {
                List<ChatMessage> old = LoadHistory();
                _sessions[0].Messages.AddRange(old);
                File.Delete(oldFile);
                ChatSessionStorage.Save(_sessions);
            }

            RenderChatList();
            SwitchSession(_sessions[0]);

            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            _history = LoadHistory();
            foreach (var m in PersistentModelList.Load())
                CreateModelCard(m.Name);   // 把模型渲染成卡片
                                           //         foreach (var m in PersistentModelList.Load())
                                           //               CreateModelCard(m.Path);   // 把模型渲染成卡片

            foreach (var m in _history)
            {
                var senderName = m.Role == "user" ? "我" : m.Assistant;
                AddMessageToUI(senderName, m.Text, m.Role == "user");

            }

        }

        private void RenderChatList()
        {
            chatlist.Children.Clear();

            foreach (ChatSession s in _sessions)
            {
                Border card = new Border();
                card.Width = 250;
                card.Height = 35;
                card.CornerRadius = new CornerRadius(18);
                card.Background = Brushes.Transparent;
                card.Cursor = Cursors.Hand;
                card.Margin = new Thickness(0, 5, 0, 0);
                card.Tag = s.Id;
                Grid g = new Grid();

                Image icon = new Image();
                icon.Source = new BitmapImage(
                    new Uri("pack://application:,,,/chat.png"));
                icon.Width = 22;
                icon.Height = 22;
                icon.HorizontalAlignment = HorizontalAlignment.Left;
                icon.Margin = new Thickness(10, 0, 0, 0);
                g.Children.Add(icon);

                TextBlock title = new TextBlock();
                title.Text = s.Title;
                title.Foreground = Brushes.White;
                title.FontSize = 13;
                title.VerticalAlignment = VerticalAlignment.Center;
                title.Margin = new Thickness(40, 0, 0, 0);
                g.Children.Add(title);
                Image delImg = new Image();
                delImg.Source = new BitmapImage(
                    new Uri("pack://application:,,,/delet.png"));
                delImg.Width = 16;
                delImg.Height = 16;

                Border delBtn = new Border();
                delBtn.Width = 30;
                delBtn.Height = 30;
                delBtn.HorizontalAlignment = HorizontalAlignment.Right;
                delBtn.VerticalAlignment = VerticalAlignment.Center;
                delBtn.Margin = new Thickness(0, 0, 8, 0);
                delBtn.Background = Brushes.Transparent;
                delBtn.BorderThickness = new Thickness(0);
                delBtn.Cursor = Cursors.Hand;
                delBtn.Child = delImg;
                delBtn.Visibility = Visibility.Collapsed;   // 默认隐藏
                delBtn.Tag = s.Id;                          // 记录会话 id
                g.Children.Add(delBtn);

                /* ---------- 事件 ---------- */

                // 鼠标进入卡片：显示删除图标
                card.MouseEnter += (sender, e) =>
                {
                    delBtn.Visibility = Visibility.Visible;
                };

                // 鼠标离开卡片：隐藏删除图标
                card.MouseLeave += (sender, e) =>
                {
                    // 如果是当前会话，保持显示
                    if (_current != null && _current.Id == (string)card.Tag)
                        delBtn.Visibility = Visibility.Visible;
                    else
                        delBtn.Visibility = Visibility.Collapsed;
                };

                // 单击卡片：切换会话
                card.MouseLeftButtonDown += (sender, e) =>
                {
                    SwitchSession(s);
                };

                // 单击删除图标：删除会话
                delBtn.MouseLeftButtonDown += (sender, e) =>
                {
                    e.Handled = true;   // 阻止卡片点击事件
                    DeleteSession(s);
                };

                card.Child = g;
                chatlist.Children.Add(card);
            }
        }

        private void DeleteSession(ChatSession toDelete)
        {
            _sessions.Remove(toDelete);

            if (_sessions.Count == 0)
            {
                ChatSession def = new ChatSession();
                def.Id = Guid.NewGuid().ToString();
                def.Title = "默认对话";
                _sessions.Add(def);
            }

            ChatSessionStorage.Save(_sessions);
            RenderChatList();
            SwitchSession(_sessions[0]);
        }

        private void SwitchSession(ChatSession session)
        {
            _current = session;

            // 高亮
            foreach (UIElement ele in chatlist.Children)
            {
                Border b = ele as Border;
                if (b != null) b.Background = Brushes.Transparent;
            }
            foreach (UIElement ele in chatlist.Children)
            {
                Border b = ele as Border;
                if (b != null && ((string)b.Tag) == session.Id)
                {
                    b.Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#252525"));
                    break;
                }
            }

            // 清空右侧并加载消息
            MessagesPanel.Children.Clear();
            foreach (ChatMessage m in session.Messages)
            {
                AddMessageToUI(
                    m.Role == "user" ? "我" : m.Assistant,
                    m.Text,
                    m.Role == "user");
            }
        }
        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
               
        }

        private void OpenWebPage(string url)
        {
            // 启动系统默认浏览器并打开URL
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true // 
            });
        }
        private static List<ChatMessage> LoadHistory()
        {
            //这个是生成一个chat_history.json文件，然后转化的
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_history.json");
            if (!File.Exists(path)) return new List<ChatMessage>();

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new List<ChatMessage>();
            }
            catch
            {
                return new List<ChatMessage>();
            }
        }

        private static void SaveHistory(List<ChatMessage> history)
        {
            var json = JsonConvert.SerializeObject(history, Formatting.Indented);
            File.WriteAllText(HistoryPath, json, Encoding.UTF8);
        }

        public class VersionResponse
        {
            public string version { get; set; }
        }


        public class VersionChecker
        {
            private readonly HttpClient _httpClient;
            private readonly string _apiUrl = "https://tiorrabox.xozv.top/update.json";

            public VersionChecker()
            {
                // 1. 创建 HttpClientHandler
                var handler = new HttpClientHandler
                {

                };

                // 2. 用配置好的 handler 初始化 HttpClient
                _httpClient = new HttpClient(handler);
                //_httpClient.Timeout = TimeSpan.FromSeconds(10);
            }


            // 异步获取版本号

            public async Task<string> GetVersionAsync()
            {
                // 在 VersionChecker 构造函数或请求前设置
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                );
                // 如果需要 Referer，也加上
                _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://tiorrabox.xozv.top/");
                try
                {
                    // 发送GET请求
                    var response = await _httpClient.GetAsync(_apiUrl);

                    // 确保响应成功
                    response.EnsureSuccessStatusCode();

                    // 读取响应内容
                    var jsonString = await response.Content.ReadAsStringAsync();

                    // 解析JSON
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true // 忽略属性名大小写
                    };
                    var versionInfo = JsonSerializer.Deserialize<VersionResponse>(jsonString, options);

                    return versionInfo?.version;
                }
                catch (HttpRequestException ex)
                {
                    // 处理网络请求异常
                    Console.WriteLine($"请求错误: {ex.Message}");
                    return null;
                }
                catch (JsonException ex)
                {
                    // 处理JSON解析异常
                    Console.WriteLine($"JSON解析错误: {ex.Message}");
                    return null;
                }
                catch (Exception ex)
                {
                    // 处理其他异常
                    Console.WriteLine($"发生错误: {ex.Message}");
                    return null;
                }
            }
        }


        // --------------- 5. 解析响应 ---------------
        public static async Task<string> ReceiveCompletionAsync(HttpResponseMessage response)
        {
            while (!Process.GetProcessesByName("llama-server").Any())
            {
                await Task.Delay(1000);
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("content", out var c))
                {
                    return c.GetString()?.Trim() ?? string.Empty;
                }
                return string.Empty;
            }
        }


        public class ChatMessage
        {
            public string Role { get; set; }
            public string Assistant { get; set; }
            public string Text { get; set; }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                grid.Margin = new Thickness(7);
                minwindow.Visibility = Visibility.Visible;
                maxwindow.Visibility = Visibility.Collapsed;
            }
            if (WindowState == WindowState.Normal)
            {
                grid.Margin = new Thickness(0);
                minwindow.Visibility = Visibility.Collapsed;
                maxwindow.Visibility = Visibility.Visible;
            }
        }
        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
            }
        }

        private void Border_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void qution_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            opcity04.Visibility = Visibility.Visible;
            powerby.Visibility = Visibility.Visible;
        }

        private void setting_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
            bushu.Background = new SolidColorBrush(Colors.Transparent);
            fastbushu.Visibility = Visibility.Collapsed;
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
            qution.Background = new SolidColorBrush(Colors.Transparent); ;
            setting.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
            setting.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
            qution.Background = new SolidColorBrush(Colors.Transparent);
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
            chat.BorderBrush = new SolidColorBrush(Colors.Transparent);
            chat.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void chat_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            gerenyemian.Visibility = Visibility.Collapsed;
            renshe.BorderBrush = new SolidColorBrush(Colors.Transparent);
            renshe.Background = new SolidColorBrush(Colors.Transparent);
            shop.Visibility = Visibility.Collapsed;
            vip.BorderBrush = new SolidColorBrush(Colors.Transparent);
            vip.Background = new SolidColorBrush(Colors.Transparent);
            bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
            bushu.Background = new SolidColorBrush(Colors.Transparent);
            fastbushu.Visibility = Visibility.Collapsed;
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
            qution.Background = new SolidColorBrush(Colors.Transparent); ;
            setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
            setting.Background = new SolidColorBrush(Colors.Transparent);
            qution.Background = new SolidColorBrush(Colors.Transparent);
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
            chat.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
            chat.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
        }



        private void AddMessageToUI(string sender, string message, bool isSelf)
        {
            // 整个消息行（横向）
            StackPanel messageContainer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 20, 0, 0),   // ← 左侧 15
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // 头像
            Border avatarBorder = new Border
            {
                CornerRadius = new CornerRadius(25),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00b96b")),
                Width = 35,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),   // 头像右侧留 10 空隙
                VerticalAlignment = VerticalAlignment.Top
            };
            Image avatarImg = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/user.png")),
                Width = 20,
                Height = 20
            };
            avatarBorder.Child = avatarImg;

            // 右侧文字区
            StackPanel textPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top
            };

            // 昵称
            TextBlock nameBlock = new TextBlock
            {
                Text = isSelf ? "我" : sender,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14
            };

            // 时间
            TextBlock timeBlock = new TextBlock
            {
                Text = DateTime.Now.ToString("yyyy/M/d"),
                Foreground = Brushes.White,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 3)
            };

            // 消息
            TextBox msgBlock = new TextBox
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),          // 去掉边框
                IsReadOnly = true
            };

            textPanel.Children.Add(nameBlock);
            textPanel.Children.Add(timeBlock);
            textPanel.Children.Add(msgBlock);

            messageContainer.Children.Add(avatarBorder);
            messageContainer.Children.Add(textPanel);

            MessagesPanel.Children.Add(messageContainer);
            ChatScrollViewer.ScrollToEnd();
        }


        public async Task<HttpResponseMessage> SendPromptAsync(string prompt,
                                                                       string model = null,   // llama.cpp 无视
                                                                       bool stream = false)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("prompt 不能为空");

            // llama.cpp /completion 请求体
            var payload = new
            {
                // ----------------------------------
                // 1. 聊天模版
                // ----------------------------------
                // 注意：llama.cpp 用 prompt 字段放「已经渲染好的」完整文本，
                //       所以下面我们要手动把模版跑一遍。
                prompt = $@"{_cfg.renshe}
<｜User｜>{prompt}<｜Assistant｜>",

                // ----------------------------------
                // 2. 采样参数
                // ----------------------------------
                temperature = _cfg.wendu,
                top_p = 0.95,
                top_k = 40,
                repeat_penalty = _cfg.chengfa,
                min_p = 0.05,
                keep_alive = 0,
                cache_prompt = true,   // 等价于 Ollama 把对话保持在上下文里
                n_predict = 1024,   // 与 num_ctx 保持一致或略小
                n_threads = 4,      // num_thread
                n_gpu_layers = 8,     // num_gpu

                // ----------------------------------
                // 3. 停止符
                // ----------------------------------
                stop = new[]
                {
        "<｜begin▁of▁sentence｜>",
        "<｜end▁of▁sentence｜>",
        "<｜User｜>",
        "<｜Assistant｜>"
    },


            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("http://localhost:8080");   // llama.cpp 默认端口
                return await client.PostAsync("/completion", content);
            }
        }



        private async void send_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _cfg.keepRounds = int.Parse(lunshu.Text);
            _cfg.renshe = renshebianxie.Text;
            if (isen.HorizontalAlignment == HorizontalAlignment.Right)
            {
                _cfg.jiyi = true;
            }
            else
            {
               _cfg.jiyi = false;
            }
            if (double.TryParse(wendu.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _cfg.wendu = v;
                SaveConfig();
            }
            if (double.TryParse(chengfa.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cf))
                _cfg.chengfa = cf;
            else { MessageBox.Show("设置 -> 模型纠正度必须是数字"); return; }

            SaveConfig();
            string tickFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tick.txt");
            string ticks;
            if (!File.Exists(tickFile))
            {
                // 文件不存在：创建并写入 1
                ticks = "1";
                File.WriteAllText(tickFile, ticks);
            }
            else
            {
                // 文件存在：读取
                ticks = File.ReadAllText(tickFile).Trim();
               
                // 如果读取结果是空，就设为 1
                if (string.IsNullOrWhiteSpace(ticks))
                {
                    ticks = "1";
                    File.WriteAllText(tickFile, ticks);   // 把修正后的值写回去
                }

                else
                {
                    int tickValue = int.Parse(ticks);
                    if (tickValue % 60 == 0)
                    {
                        zanzhutanchuang.Visibility = Visibility.Visible;
                        cishu.Text = $"{ticks}次";
                        opcity04.Visibility = Visibility.Visible;
                    }
                    tickValue--;
                    ticks = tickValue.ToString();
                    File.WriteAllText(tickFile, ticks);
                }
            }

            string message = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                MessageBox.Show("请输入内容！");
                return;
            }

            // llama 进程检查
            if (!Process.GetProcessesByName("llama-server").Any())
            {
                MessageBox.Show("请先点击左侧查看是否添加模型，若添加，请先点击添加的模型启动服务，若你已启动服务，请等待模型加载一会");
                return;
            }

            // 1. 记录用户消息
            _history.Add(new ChatMessage { Role = "user", Text = message });
            AddMessageToUI("我", message, true);
            input.Clear();
            int lun = int.Parse(lunshu.Text);   // 假设用户填的是整数
            int keep = _cfg.keepRounds * 2;
            var memory = _history.Skip(Math.Max(0, _history.Count - keep)).ToList();

            // 3. 照旧发出去
            string historyJson = JsonSerializer.Serialize(memory);
            // 拼完整 prompt（系统+历史）
            if (_memoryEnabled == true)
            {
                var prompt = historyJson;
            var resp = await SendPromptAsync(prompt + "以上是历史对话，请牢记，以下是当前问题:" + message);
            string aiAnswer = await ReceiveCompletionAsync(resp);
            // 记录 AI 回复
            _history.Add(new ChatMessage
            {
                Role = "assistant",
                Text = aiAnswer,
                Assistant = CurrentModelName
            });
            AddMessageToUI(CurrentModelName, aiAnswer, false);

            //保存
            SaveHistory(_history);
            }
            else
            {
                var resp = await SendPromptAsync(message);
                string aiAnswer = await ReceiveCompletionAsync(resp);
                _history.Add(new ChatMessage
                {
                    Role = "assistant",
                    Text = aiAnswer,
                    Assistant = CurrentModelName
                });
                AddMessageToUI(CurrentModelName, aiAnswer, false);

                // 5. 保存
                SaveHistory(_history);
            }
        }



        public class ChatHistoryRecord
        {
            public string Title { get; set; }
            public List<ChatMessage> Messages { get; set; }
        }


        private async void bushu_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            gerenyemian.Visibility = Visibility.Collapsed;
            renshe.BorderBrush = new SolidColorBrush(Colors.Transparent);
            renshe.Background = new SolidColorBrush(Colors.Transparent);
            chat.BorderBrush = new SolidColorBrush(Colors.Transparent);
            chat.Background = new SolidColorBrush(Colors.Transparent);
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
            qution.Background = new SolidColorBrush(Colors.Transparent); ;
            setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
            setting.Background = new SolidColorBrush(Colors.Transparent);
            qution.Background = new SolidColorBrush(Colors.Transparent);
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
            bushu.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
            bushu.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
            fastbushu.Visibility = Visibility.Visible;
            shop.Visibility = Visibility.Collapsed;
            vip.BorderBrush = new SolidColorBrush(Colors.Transparent);
            vip.Background = new SolidColorBrush(Colors.Transparent);
            await Task.Delay(100);
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            bool hasWiFi = false;
            foreach (NetworkInterface ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    hasWiFi = true;
                    break;
                }
            }
            if (hasWiFi)
            {
                Loaded += OnWindowLoaded;
                var versionChecker = new VersionChecker();
                string version = await versionChecker.GetVersionAsync();
                if (version != "1.2.4")
                {
                    MessageBox.Show($"版本已更新到{version}，当前版本1.2.4，请前往官网下载更新");
                    string url = "https://tiorrabox.xozv.top"; // 要打开的网页地址
                    OpenWebPage(url);
                    gerenyemian.Visibility = Visibility.Collapsed;
                    fastbushu.Visibility = Visibility.Collapsed;
                    qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
                    qution.Background = new SolidColorBrush(Colors.Transparent); ;
                    setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    setting.Background = new SolidColorBrush(Colors.Transparent);
                    qution.Background = new SolidColorBrush(Colors.Transparent);
                    qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    bushu.Background = new SolidColorBrush(Colors.Transparent);
                    chat.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
                    chat.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
                }
                else
                {
                }
            }


            else
            {
            }
        }


        private long GetAvailableSpace(string folderPath)
        {
            DriveInfo driveInfo = new DriveInfo(System.IO.Path.GetPathRoot(folderPath));
            return driveInfo.AvailableFreeSpace;
        }


        private void yijianbushu_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //无用，删除可能报错(史山)
        }


        private async void deepseekr1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. Wi-Fi 检测
            if (!IsWifiConnected())
            {
                MessageBox.Show("当前未连接到 Wi-Fi，请连接 Wi-Fi 后再试");
                SetErrorUI();
                return;
            }

            // 2. 版本检查
            VersionChecker versionChecker = new VersionChecker();
            string version = await versionChecker.GetVersionAsync();
            if (version != "1.2.4")
            {
                MessageBox.Show(string.Format("版本已更新到 {0}，当前版本 1.2.4，请前往官网下载更新", version));
                OpenWebPage("https://tiorrabox.xozv.top ");
                SetErrorUI();
                return;
            }

            // 3. 选文件夹
            string folder = PromptUserForFolder();
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show("未选择文件夹，下载取消。");
                return;
            }

            // 4. 重置 UI & 下载量
            this.Dispatcher.BeginInvoke((Action)(() => { tbtext.Text = "已下载：0 KB"; }));

            // 5. 复制 server
            string sourceServer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server");
            string targetServer = Path.Combine(folder, "server");
            if (!Directory.Exists(targetServer))
                DirectoryCopy(sourceServer, targetServer);

            // 6. 写出嵌入 license.zip
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepseeklicense.zip");
            if (!File.Exists(zipPath))
            {
                const string resName = "TiorraBox.resource.deepseeklicense.zip";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("找不到嵌入资源：" + resName);
                    using (FileStream fs = File.Create(zipPath))
                        await stream.CopyToAsync(fs);
                }
            }

            // 7. 解压
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string fullPath = Path.Combine(folder, SanitizeFileName(entry.Name));
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }

            // 8. 进入监控 & 下载
            bushujiemian.Visibility = Visibility.Visible;
            bushuchenggong.Text = "正在部署..";
            quxiao.Visibility = Visibility.Visible;
            queding.Visibility = Visibility.Collapsed;
            tixingdengdai.Visibility = Visibility.Visible;
            opcity04.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();

            // 8.1 启动监控
            Task monitor = MonitorDownloadProgress(folder, _cts.Token);

            // 8.2 真正下载
            List<string> urls = new List<string>
        {
                    "https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00001-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00002-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00003-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00004-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00005-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00006-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00007-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00008-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00009-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00010-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00011-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00012-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00013-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00014-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00015-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00016-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00017-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00018-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00019-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00020-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00021-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00022-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00023-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00024-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00025-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00026-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00027-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00028-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00029-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00030-of-00030.gguf "
        };
            bool allOk = await DownloadMultipleFilesAsync(folder, urls);

            // 9. 结束
            _cts.Cancel();
            try { await monitor; } catch { /* 忽略 */ }

            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                quxiao.Visibility = Visibility.Collapsed;
                queding.Visibility = Visibility.Visible;
                tixingdengdai.Visibility = Visibility.Collapsed;
                bushuchenggong.Text = allOk ? "部署成功！" : "部署失败(部分或全部文件下载失败)。";
            }));
        }
private string PromptUserForFolder()
    {
        var dlg = new VistaFolderBrowserDialog();
        return dlg.ShowDialog() == true ? dlg.SelectedPath : null;
    }


        private async Task<bool> DownloadMultipleFilesAsync(string folder, List<string> downloadUrls)
        {
            const int BUFFER = 45000;             

            if (_httpClient == null)
            {
                _httpClient = new HttpClient(new HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                             System.Net.DecompressionMethods.Deflate
                }, true);
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                _httpClient.DefaultRequestHeaders.Add("Referer", "https://hf-mirror.com");
            }

            CancellationToken token = _cts.Token;
            Task progressTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    long totalBytes = 0;
                    try
                    {
                        foreach (string f in Directory.EnumerateFiles(folder, "*.gguf"))
                            totalBytes += new FileInfo(f).Length;
                    }
                    catch { /* 忽略 */ }
                    this.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        tbtext.Text = "已下载：" + (totalBytes / 1024).ToString() + " KB";
                    }));
                    await Task.Delay(500, token);
                }
            }, token);

            bool allOk = true;
            try
            {
                foreach (string url in downloadUrls)
                {
                    string rawName = Path.GetFileName(url.Split('?')[0]);
                    string fileName = SanitizeFileName(rawName);
                    string filePath = Path.Combine(folder, fileName);

                    /* 断点续传 */
                    long startOffset = 0;
                    if (File.Exists(filePath))
                        startOffset = new FileInfo(filePath).Length;

                    HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
                    if (startOffset > 0)
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startOffset, null);

                    HttpResponseMessage resp = await _httpClient.SendAsync(req,
                        HttpCompletionOption.ResponseHeadersRead, token);
                    if (!resp.IsSuccessStatusCode)
                    {
                        allOk = false;
                        break;
                    }

                    /* 磁盘空间二次检查 */
                    long contentLen = resp.Content.Headers.ContentLength ?? 0;
                    long needSpace = contentLen + 50 * 1024 * 1024;
                    if (GetAvailableSpace(folder) < needSpace)
                    {
                        MessageBox.Show("磁盘空间不足，无法继续下载。");
                        allOk = false;
                        break;
                    }

                    /* 流式拷贝*/
                    using (Stream netStream = await resp.Content.ReadAsStreamAsync())
                    using (FileStream fs = new FileStream(filePath, FileMode.Append,
                                                         FileAccess.Write, FileShare.None, BUFFER, useAsync: true))
                    {
                        await netStream.CopyToAsync(fs, BUFFER, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                allOk = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("下载出错：" + ex.Message);
                allOk = false;
            }
            finally
            {
                _cts.Cancel();
                try { await progressTask; } catch { /* 忽略 */ }
            }

            return allOk;
        }
        private long _lastBytes;
        private async Task MonitorDownloadProgress(string folder, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    long bytes = 0;
                    foreach (string f in Directory.EnumerateFiles(folder, "*.gguf"))
                        bytes += new FileInfo(f).Length;
                    this.Dispatcher.BeginInvoke((Action)(() => { tbtext.Text = "已下载：" + (bytes / 1024).ToString() + " KB"; }));
                }
                catch { /* 忽略占用 */ }
                await Task.Delay(1000, token);
            }
        }
        private static bool IsWifiConnected()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    ni.OperationalStatus == OperationalStatus.Up)
                    return true;
            return false;
        }
        private async void DeepSeekR1DistillQwen15B_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. Wi-Fi 检测
            if (!IsWifiConnected())
            {
                MessageBox.Show("当前未连接到 Wi-Fi，请连接 Wi-Fi 后再试");
                SetErrorUI();
                return;
            }

            // 2. 版本检查
            VersionChecker versionChecker = new VersionChecker();
            string version = await versionChecker.GetVersionAsync();
            if (version != "1.2.4")
            {
                MessageBox.Show(string.Format("版本已更新到 {0}，当前版本 1.2.4，请前往官网下载更新", version));
                OpenWebPage("https://tiorrabox.xozv.top");
                SetErrorUI();
                return;
            }

            // 3. 选文件夹
            string folder = PromptUserForFolder();
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show("未选择文件夹，下载取消。");
                return;
            }

            // 4. 重置 UI & 下载量
            this.Dispatcher.BeginInvoke((Action)(() => { tbtext.Text = "已下载：0 KB"; }));

            // 5. 复制 server
            string sourceServer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server");
            string targetServer = Path.Combine(folder, "server");
            if (!Directory.Exists(targetServer))
                DirectoryCopy(sourceServer, targetServer);

            // 6. 写出嵌入 license.zip
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepseeklicense.zip");
            if (!File.Exists(zipPath))
            {
                const string resName = "TiorraBox.resource.deepseeklicense.zip";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("找不到嵌入资源：" + resName);
                    using (FileStream fs = File.Create(zipPath))
                        await stream.CopyToAsync(fs);
                }
            }

            // 7. 解压
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string fullPath = Path.Combine(folder, SanitizeFileName(entry.Name));
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }

            // 8. 进入监控 & 下载
            bushujiemian.Visibility = Visibility.Visible;
            bushuchenggong.Text = "正在部署..";
            quxiao.Visibility = Visibility.Visible;
            queding.Visibility = Visibility.Collapsed;
            tixingdengdai.Visibility = Visibility.Visible;
            opcity04.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();

            // 8.1 启动监控
            Task monitor = MonitorDownloadProgress(folder, _cts.Token);

            // 8.2 真正下载
            List<string> urls = new List<string>
        {
            "https://hf-mirror.com/unsloth/DeepSeek-R1-Distill-Qwen-1.5B-GGUF/resolve/main/DeepSeek-R1-Distill-Qwen-1.5B-Q8_0.gguf?download=true"
        };
            bool allOk = await DownloadMultipleFilesAsync(folder, urls);

            // 9. 结束
            _cts.Cancel();
            try { await monitor; } catch { /* 忽略 */ }

            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                quxiao.Visibility = Visibility.Collapsed;
                queding.Visibility = Visibility.Visible;
                tixingdengdai.Visibility = Visibility.Collapsed;
                bushuchenggong.Text = allOk ? "部署成功！" : "部署失败(部分或全部文件下载失败)。";
            }));
        }
        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private void SetErrorUI()
        {
            fastbushu.Visibility = Visibility.Collapsed;
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
            qution.Background = new SolidColorBrush(Colors.Transparent);
            setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
            setting.Background = new SolidColorBrush(Colors.Transparent);
            bushu.Background = new SolidColorBrush(Colors.Transparent);
            bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
            chat.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
            chat.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
        }



        private async void deepseekr10528_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. Wi-Fi 检测
            if (!IsWifiConnected())
            {
                MessageBox.Show("当前未连接到 Wi-Fi，请连接 Wi-Fi 后再试");
                SetErrorUI();
                return;
            }

            // 2. 版本检查
            VersionChecker versionChecker = new VersionChecker();
            string version = await versionChecker.GetVersionAsync();
            if (version != "1.2.4")
            {
                MessageBox.Show(string.Format("版本已更新到 {0}，当前版本 1.2.4，请前往官网下载更新", version));
                OpenWebPage("https://tiorrabox.xozv.top ");
                SetErrorUI();
                return;
            }

            // 3. 选文件夹
            string folder = PromptUserForFolder();
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show("未选择文件夹，下载取消。");
                return;
            }

            // 4. 重置 UI & 下载量
            this.Dispatcher.BeginInvoke((Action)(() => { tbtext.Text = "已下载：0 KB"; }));

            // 5. 复制 server
            string sourceServer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server");
            string targetServer = Path.Combine(folder, "server");
            if (!Directory.Exists(targetServer))
                DirectoryCopy(sourceServer, targetServer);

            // 6. 写出嵌入 license.zip
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepseeklicense.zip");
            if (!File.Exists(zipPath))
            {
                const string resName = "TiorraBox.resource.deepseeklicense.zip";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("找不到嵌入资源：" + resName);
                    using (FileStream fs = File.Create(zipPath))
                        await stream.CopyToAsync(fs);
                }
            }

            // 7. 解压
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string fullPath = Path.Combine(folder, SanitizeFileName(entry.Name));
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }

            // 8. 进入监控 & 下载
            bushujiemian.Visibility = Visibility.Visible;
            bushuchenggong.Text = "正在部署..";
            quxiao.Visibility = Visibility.Visible;
            queding.Visibility = Visibility.Collapsed;
            tixingdengdai.Visibility = Visibility.Visible;
            opcity04.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();

            // 8.1 启动监控
            Task monitor = MonitorDownloadProgress(folder, _cts.Token);

            // 8.2 真正下载
            List<string> urls = new List<string>
        {
                    "https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00001-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00002-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00003-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00004-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00005-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00006-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00007-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00008-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00009-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00010-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00011-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00012-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00013-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00014-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00015-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00016-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00017-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00018-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00019-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00020-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00021-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00022-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00023-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00024-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00025-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00026-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00027-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00028-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00029-of-00030.gguf ",
"https://hf-mirror.com/unsloth/DeepSeek-R1-0528-GGUF/resolve/main/BF16/DeepSeek-R1-0528-BF16-00030-of-00030.gguf "
        };
            bool allOk = await DownloadMultipleFilesAsync(folder, urls);

            // 9. 结束
            _cts.Cancel();
            try { await monitor; } catch { /* 忽略 */ }

            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                quxiao.Visibility = Visibility.Collapsed;
                queding.Visibility = Visibility.Visible;
                tixingdengdai.Visibility = Visibility.Collapsed;
                bushuchenggong.Text = allOk ? "部署成功！" : "部署失败(部分或全部文件下载失败)。";
            }));
        }

        private async void DeepSeekR1DistillQwen32B_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. Wi-Fi 检测
            if (!IsWifiConnected())
            {
                MessageBox.Show("当前未连接到 Wi-Fi，请连接 Wi-Fi 后再试");
                SetErrorUI();
                return;
            }

            // 2. 版本检查
            VersionChecker versionChecker = new VersionChecker();
            string version = await versionChecker.GetVersionAsync();
            if (version != "1.2.4")
            {
                MessageBox.Show(string.Format("版本已更新到 {0}，当前版本 1.2.4，请前往官网下载更新", version));
                OpenWebPage("https://tiorrabox.xozv.top ");
                SetErrorUI();
                return;
            }

            // 3. 选文件夹
            string folder = PromptUserForFolder();
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show("未选择文件夹，下载取消。");
                return;
            }

            // 4. 重置 UI & 下载量
            this.Dispatcher.BeginInvoke((Action)(() => { tbtext.Text = "已下载：0 KB"; }));

            // 5. 复制 server
            string sourceServer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server");
            string targetServer = Path.Combine(folder, "server");
            if (!Directory.Exists(targetServer))
                DirectoryCopy(sourceServer, targetServer);

            // 6. 写出嵌入 license.zip
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepseeklicense.zip");
            if (!File.Exists(zipPath))
            {
                const string resName = "TiorraBox.resource.deepseeklicense.zip";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("找不到嵌入资源：" + resName);
                    using (FileStream fs = File.Create(zipPath))
                        await stream.CopyToAsync(fs);
                }
            }

            // 7. 解压
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string fullPath = Path.Combine(folder, SanitizeFileName(entry.Name));
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }

            // 8. 进入监控 & 下载
            bushujiemian.Visibility = Visibility.Visible;
            bushuchenggong.Text = "正在部署..";
            quxiao.Visibility = Visibility.Visible;
            queding.Visibility = Visibility.Collapsed;
            tixingdengdai.Visibility = Visibility.Visible;
            opcity04.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();

            // 8.1 启动监控
            Task monitor = MonitorDownloadProgress(folder, _cts.Token);

            // 8.2 真正下载
            List<string> urls = new List<string>
        {
                "https://hf-mirror.com/unsloth/DeepSeek-R1-Distill-Qwen-32B-GGUF/resolve/main/DeepSeek-R1-Distill-Qwen-32B-F16/DeepSeek-R1-Distill-Qwen-32B-F16-00001-of-00002.gguf?download=true",
                "https://hf-mirror.com/unsloth/DeepSeek-R1-Distill-Qwen-32B-GGUF/resolve/main/DeepSeek-R1-Distill-Qwen-32B-F16/DeepSeek-R1-Distill-Qwen-32B-F16-00002-of-00002.gguf?download=true"
        };
            bool allOk = await DownloadMultipleFilesAsync(folder, urls);

            // 9. 结束
            _cts.Cancel();
            try { await monitor; } catch { /* 忽略 */ }

            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                quxiao.Visibility = Visibility.Collapsed;
                queding.Visibility = Visibility.Visible;
                tixingdengdai.Visibility = Visibility.Collapsed;
                bushuchenggong.Text = allOk ? "部署成功！" : "部署失败(部分或全部文件下载失败)。";
            }));
        }

        private async void DeepSeekR1DistillQwen7B_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. Wi-Fi 检测
            if (!IsWifiConnected())
            {
                MessageBox.Show("当前未连接到 Wi-Fi，请连接 Wi-Fi 后再试");
                SetErrorUI();
                return;
            }

            // 2. 版本检查
            VersionChecker versionChecker = new VersionChecker();
            string version = await versionChecker.GetVersionAsync();
            if (version != "1.2.4")
            {
                MessageBox.Show(string.Format("版本已更新到 {0}，当前版本 1.2.4，请前往官网下载更新", version));
                OpenWebPage("https://tiorrabox.xozv.top");
                SetErrorUI();
                return;
            }

            // 3. 选文件夹
            string folder = PromptUserForFolder();
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show("未选择文件夹，下载取消。");
                return;
            }

            // 4. 重置 UI & 下载量
            this.Dispatcher.BeginInvoke((Action)(() => { tbtext.Text = "已下载：0 KB"; }));

            // 5. 复制 server
            string sourceServer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server");
            string targetServer = Path.Combine(folder, "server");
            if (!Directory.Exists(targetServer))
                DirectoryCopy(sourceServer, targetServer);

            // 6. 写出嵌入 license.zip
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepseeklicense.zip");
            if (!File.Exists(zipPath))
            {
                const string resName = "TiorraBox.resource.deepseeklicense.zip";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("找不到嵌入资源：" + resName);
                    using (FileStream fs = File.Create(zipPath))
                        await stream.CopyToAsync(fs);
                }
            }

            // 7. 解压
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string fullPath = Path.Combine(folder, SanitizeFileName(entry.Name));
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }

            // 8. 进入监控 & 下载
            bushujiemian.Visibility = Visibility.Visible;
            bushuchenggong.Text = "正在部署..";
            quxiao.Visibility = Visibility.Visible;
            queding.Visibility = Visibility.Collapsed;
            tixingdengdai.Visibility = Visibility.Visible;
            opcity04.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();

            // 8.1 启动监控
            Task monitor = MonitorDownloadProgress(folder, _cts.Token);

            // 8.2 真正下载
            List<string> urls = new List<string>
        {
            "https://hf-mirror.com/unsloth/DeepSeek-R1-Distill-Qwen-7B-GGUF/resolve/main/DeepSeek-R1-Distill-Qwen-7B-F16.gguf?download=true"
        };
            bool allOk = await DownloadMultipleFilesAsync(folder, urls);

            // 9. 结束
            _cts.Cancel();
            try { await monitor; } catch { /* 忽略 */ }

            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                quxiao.Visibility = Visibility.Collapsed;
                queding.Visibility = Visibility.Visible;
                tixingdengdai.Visibility = Visibility.Collapsed;
                bushuchenggong.Text = allOk ? "部署成功！" : "部署失败(部分或全部文件下载失败)。";
            }));
        }

        private void closeinputmodelname_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            backgroundopcity.Visibility = Visibility.Collapsed;
            modelinputborder.Visibility = Visibility.Collapsed;
        }

        private void openinputmodelname_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            backgroundopcity.Visibility = Visibility.Visible;
            modelinputborder.Visibility = Visibility.Visible;
        }
        private int _modelTop = 5;

        private static void DirectoryCopy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                DirectoryCopy(dir, Path.Combine(target, Path.GetFileName(dir)));
        }


        private void addmodel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {


            var dlg = new VistaFolderBrowserDialog();
            if (dlg.ShowDialog() != true) return;

            string modelDir = dlg.SelectedPath;
            string modelName = modelnameinput.Text.Trim();
            if (string.IsNullOrWhiteSpace(modelName)) return;

            backgroundopcity.Visibility = Visibility.Collapsed;
            modelinputborder.Visibility = Visibility.Collapsed;

            // server copy
            var sourceServer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server");
            var targetServer = Path.Combine(modelDir, "server");
            if (!Directory.Exists(targetServer))
                DirectoryCopy(sourceServer, targetServer);
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                          "deepseeklicense.zip");

            // 2. 如果磁盘上还没有 zip，则从嵌入资源写一份
            if (!File.Exists(zipPath))
            {
                // 资源名 = 命名空间名 + 文件夹名 + 文件名
                string resourceName = "TiorraBox.resource.deepseeklicense.zip"; // 按实际命名空间改
                using (Stream stream = Assembly.GetExecutingAssembly()
                                               .GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("找不到嵌入资源：" + resourceName);

                    using (FileStream fileStream = File.Create(zipPath))
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }

            // 3. 解压
            string targetDir = modelDir;
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string fullPath = Path.Combine(targetDir, entry.FullName);

                    if (string.IsNullOrEmpty(entry.Name)) // 目录
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }


            // writing json
            var list = ModelStorage.Load();
            list.RemoveAll(m => m.Name == modelName);
            list.Add(new ModelPath { Name = modelName, Path = modelDir });
            ModelStorage.Save(list);

            //create ui
            CreateModelCard(modelName);
            modelnameinput.Clear();
        }



        private void CreateModelCard(string modelName)
        {
            // 卡片总容器
            var border = new Border
            {
                Width = 250,
                Height = 35,
                CornerRadius = new CornerRadius(18),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, _modelTop, 0, 0),
                Tag = modelName
            };

            // 内部网格
            var grid = new Grid();

            // logo
            grid.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/deepseeklogo.png")),
                Width = 35,
                Height = 35,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            });

            // 名字
            grid.Children.Add(new TextBlock
            {
                Text = modelName,
                Foreground = Brushes.White,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40, 0, 0, 0)
            });

            // 删除按钮（默认隐藏）
            var btnDel = new Border
            {
                Width = 30,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = modelName,
                Visibility = Visibility.Collapsed
            };

            // 用图片
            btnDel.Child = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/delet.png")),
                Width = 16,
                Height = 16
            };
            grid.Children.Add(btnDel);

            border.Child = grid;

            // 鼠标进入/离开
            border.MouseEnter += (_, __) => btnDel.Visibility = Visibility.Visible;
            border.MouseLeave += (_, __) => btnDel.Visibility = Visibility.Collapsed;

            // 点击卡片 → 高亮 + 启动
            border.MouseLeftButtonDown += async (s, _) =>
            {
                foreach (Border b in modellist.Children)
                    b.Background = Brushes.Transparent;
                ((Border)s).Background =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252525"));

                var mp = ModelStorage.Load()
                         .FirstOrDefault(m => m.Name == (string)((Border)s).Tag);
                if (mp == null) return;

                var exe = Path.Combine(mp.Path, "server", "llama-server.exe");
                var gguf = Directory.EnumerateFiles(mp.Path, "*.gguf").FirstOrDefault();
                if (gguf == null)
                {
                    MessageBox.Show("未检测到.gguf格式模型文件，请重新下载。\r\n不支持 safetensors，请使用 .gguf 格式。");
                    return;
                }

                CurrentModelName = modelName;

                // 先关掉旧服务
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Stop-Process -Name 'llama-server' -Force -ErrorAction SilentlyContinue\"",
                   CreateNoWindow = true,
                    UseShellExecute = false
               })?.WaitForExit();

                // 启动新服务
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-m \"{gguf}\" -c 4096 --port 8080",
                    WorkingDirectory = mp.Path,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            };

            // 删除按钮事件
            btnDel.MouseLeftButtonDown += (_, __) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Stop-Process -Name 'llama-server' -Force -ErrorAction SilentlyContinue\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();
                // 1. 从 JSON 移除
                var list = ModelStorage.Load();
                list.RemoveAll(m => m.Name == modelName);
                ModelStorage.Save(list);

                // 2. 从 UI 移除
                modellist.Children.Remove(border);
            };

            modellist.Children.Add(border);
            _modelTop += 1;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _cfg.keepRounds = int.Parse(lunshu.Text);
            _cfg.renshe = renshebianxie.Text;
            if (isen.HorizontalAlignment == HorizontalAlignment.Right)
            {
                _cfg.jiyi = true;
            }
            else
            {
                _cfg.jiyi = false;
            }
            if (double.TryParse(wendu.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _cfg.wendu = v;
                SaveConfig();
            }
            if (double.TryParse(chengfa.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cf))
                _cfg.chengfa = cf;

            SaveConfig();
            ChatSessionStorage.Save(_sessions);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"& { Stop-Process -Name 'llama-server' -Force -ErrorAction SilentlyContinue }\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using (var proc = Process.Start(psi))
            {
                proc?.WaitForExit(); // 等待 PowerShell 脚本执行完毕
            }
        }
        internal static class PersistentModelList
        {
            private static readonly string FilePath =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modelpath.json");

            public static List<ModelPath> Load()
            {
                if (!File.Exists(FilePath)) return new List<ModelPath>();
                var list = JsonConvert.DeserializeObject<List<ModelPath>>(File.ReadAllText(FilePath))
             ?? new List<ModelPath>();
                // 启动时一次性过滤掉已失效的文件夹
                list.RemoveAll(m => !Directory.Exists(m.Path));
                Save(list);               // 把清理后的结果写回
                return list;
            }

            public static void Save(IEnumerable<ModelPath> list) =>
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(list, Formatting.Indented));
        }

        private void huati_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _cfg.keepRounds = int.Parse(lunshu.Text);
            _cfg.renshe = renshebianxie.Text;
            if (isen.HorizontalAlignment == HorizontalAlignment.Right)
            {
                _cfg.jiyi = true;
            }
            else
            {
              _cfg.jiyi = false;
           }
            if (double.TryParse(wendu.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _cfg.wendu = v;
                SaveConfig();
            }
            if (double.TryParse(chengfa.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cf))
                _cfg.chengfa = cf;
            else { MessageBox.Show("设置 -> 模型纠正度必须是数字"); return; }
            SaveConfig();
            huatitext.Foreground = new SolidColorBrush(Colors.White);
            huatiborder.Visibility = Visibility.Visible;
            moxingtext.Foreground = new SolidColorBrush(Colors.Gray);
            moxingborder.Visibility = Visibility.Collapsed;
            shezhitext.Foreground = new SolidColorBrush(Colors.Gray);
            shezhiborder.Visibility = Visibility.Collapsed;
        }

        private void moxing_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _cfg.keepRounds = int.Parse(lunshu.Text);
            _cfg.renshe = renshebianxie.Text;
            if (isen.HorizontalAlignment == HorizontalAlignment.Right)
            {
                _cfg.jiyi = true;
            }
            else
            {
                _cfg.jiyi = false;
            }
            if (double.TryParse(wendu.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _cfg.wendu = v;
                SaveConfig();
            }
            if (double.TryParse(chengfa.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cf))
                _cfg.chengfa = cf;
            else { MessageBox.Show("设置 -> 模型纠正度必须是数字"); return; }

            SaveConfig();
            huatitext.Foreground = new SolidColorBrush(Colors.Gray);
            huatiborder.Visibility = Visibility.Collapsed;
            moxingtext.Foreground = new SolidColorBrush(Colors.White);
            moxingborder.Visibility = Visibility.Visible;
            shezhitext.Foreground = new SolidColorBrush(Colors.Gray);
            shezhiborder.Visibility = Visibility.Collapsed;
        }

        private void addchat_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            string title = chatnameinput.Text.Trim();
            if (string.IsNullOrEmpty(title)) return;

            ChatSession ns = new ChatSession();
            ns.Id = Guid.NewGuid().ToString();
            ns.Title = title;

            _sessions.Add(ns);
            ChatSessionStorage.Save(_sessions);

            RenderChatList();
            SwitchSession(ns);

            backgroundopcity.Visibility = Visibility.Collapsed;
            chatinputborder.Visibility = Visibility.Collapsed;
        }

        private void closeinputchatname_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            backgroundopcity.Visibility = Visibility.Collapsed;
            chatinputborder.Visibility = Visibility.Collapsed;
        }

        private void openaddchat_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            backgroundopcity.Visibility = Visibility.Visible;
            chatinputborder.Visibility = Visibility.Visible;
            chatnameinput.Text = string.Empty;   // 清空
        }

        private void Border_MouseEnter_1(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2b2b2b"));
            }
        }

        private void Border_MouseLeave_1(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void close_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.Close();
        }

        private void maxwindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.WindowState = WindowState.Maximized;
            minwindow.Visibility = Visibility.Visible;
            maxwindow.Visibility = Visibility.Collapsed;
        }

        private void hidewindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void minwindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.WindowState = WindowState.Normal;
            minwindow.Visibility = Visibility.Collapsed;
            maxwindow.Visibility = Visibility.Visible;
        }

        private void shezhi_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _cfg.keepRounds = int.Parse(lunshu.Text);
            renshebianxie.Text = _cfg.renshe;
            if (isen.HorizontalAlignment == HorizontalAlignment.Right)
            {
                _cfg.jiyi = true;
            }
            else
            {
                _cfg.jiyi = false;
            }
            if (double.TryParse(wendu.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _cfg.wendu = v;
                SaveConfig();
            }
            if (double.TryParse(chengfa.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cf))
                _cfg.chengfa = cf;
            else { MessageBox.Show("设置 -> 模型纠正度必须是数字"); return; }

            SaveConfig();
            LoadOrCreateConfig();
            LoadConfigToControls();
            huatitext.Foreground = new SolidColorBrush(Colors.Gray);
            huatiborder.Visibility = Visibility.Collapsed;
            shezhitext.Foreground = new SolidColorBrush(Colors.White);
            shezhiborder.Visibility = Visibility.Visible;
            moxingtext.Foreground = new SolidColorBrush(Colors.Gray);
            moxingborder.Visibility = Visibility.Collapsed;
        }


        private void openjiyibaocun_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_memoryEnabled == false)
            {
                openjiyibaocun.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00b96b"));
                _memoryEnabled = true;
                _cfg.jiyi = true;
                isen.HorizontalAlignment = HorizontalAlignment.Right;
            }
            else
            {
                _cfg.jiyi = false;
                openjiyibaocun.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
                isen.HorizontalAlignment = HorizontalAlignment.Left;
                _memoryEnabled = false;
            }
        }

        private void meiwenti_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            zanzhutanchuang.Visibility = Visibility.Collapsed;
            opcity04.Visibility = Visibility.Collapsed;
            string url = "https://afdian.com/a/tiorrabox";

            // 兼容 .NET Core / .NET 5+ 的写法
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true   // 必须设为 true 才能用默认浏览器
            });
        }

        private void xiaciyiding_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            zanzhutanchuang.Visibility = Visibility.Collapsed;
            opcity04.Visibility = Visibility.Collapsed;

        }

        private void queding_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            bushujiemian.Visibility = Visibility.Collapsed;
            opcity04.Visibility = Visibility.Collapsed;
        }
        private CancellationTokenSource _cts;
        private void quxiao_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _cts?.Cancel();
            opcity04.Visibility = Visibility.Collapsed;
            bushujiemian.Visibility = Visibility.Collapsed;
        }

        private void saveconfig_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(isen.HorizontalAlignment == HorizontalAlignment.Right)
            {
                _cfg.jiyi = true;
            }
            else
            {
  _cfg.jiyi = false;
   }
            if (double.TryParse(wendu.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _cfg.wendu = v;
                SaveConfig();
            }
        }

        private void yinian_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            yinian.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1f1f1f"));
            liugeyue.Background = new SolidColorBrush(Colors.Transparent);
            yigeyue.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void liugeyue_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            yinian.Background = new SolidColorBrush(Colors.Transparent);
            liugeyue.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1f1f1f"));
            yigeyue.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void yigeyue_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            yinian.Background = new SolidColorBrush(Colors.Transparent);
            liugeyue.Background = new SolidColorBrush(Colors.Transparent);
            yigeyue.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1f1f1f"));
        }

        private void aifadian_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            
        }

        private void zhifu_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            string url = "https://afdian.com/a/tiorrabox";

            // 兼容 .NET Core / .NET 5+ 的写法
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true   // 必须设为 true 才能用默认浏览器
            });
        }

        private void closebuy_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            buybackground.Visibility = Visibility.Collapsed;
            buyborder.Visibility = Visibility.Collapsed;
        }

        private void buyvip1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            buybackground.Visibility = Visibility.Visible;
            buyborder.Visibility = Visibility.Visible;
        }

        private void goumai_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void goumai_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
        {

        }

        private void vip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            gerenyemian.Visibility = Visibility.Collapsed;
            shop.Visibility = Visibility.Visible;
            chat.BorderBrush = new SolidColorBrush(Colors.Transparent);
            chat.Background = new SolidColorBrush(Colors.Transparent);
            bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
            bushu.Background = new SolidColorBrush(Colors.Transparent);
            fastbushu.Visibility = Visibility.Collapsed;
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
            qution.Background = new SolidColorBrush(Colors.Transparent); ;
            setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
            setting.Background = new SolidColorBrush(Colors.Transparent);
            qution.Background = new SolidColorBrush(Colors.Transparent);
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
            vip.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
            vip.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
        }

        private async void renshe_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            bool hasWiFi = false;
            foreach (NetworkInterface ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    hasWiFi = true;
                    break;
                }
            }
            if (hasWiFi)
            {
                Loaded += OnWindowLoaded;
                var versionChecker = new VersionChecker();
                string version = await versionChecker.GetVersionAsync();
                if (version != "1.2.4")
                {
                    MessageBox.Show($"版本已更新到{version}，当前版本1.2.4，请前往官网下载更新");
                    string url = "https://tiorrabox.xozv.top"; // 要打开的网页地址
                    OpenWebPage(url);
                    fastbushu.Visibility = Visibility.Collapsed;
                    gerenyemian.Visibility = Visibility.Collapsed;
                    qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
                    qution.Background = new SolidColorBrush(Colors.Transparent); ;
                    setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    setting.Background = new SolidColorBrush(Colors.Transparent);
                    qution.Background = new SolidColorBrush(Colors.Transparent);
                    qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    bushu.Background = new SolidColorBrush(Colors.Transparent);
                    chat.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
                    chat.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
                }
                else
                {
                    geren.Visibility = Visibility.Collapsed;
                    chat.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    chat.Background = new SolidColorBrush(Colors.Transparent);
                    qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
                    qution.Background = new SolidColorBrush(Colors.Transparent); ;
                    setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    setting.Background = new SolidColorBrush(Colors.Transparent);
                    qution.Background = new SolidColorBrush(Colors.Transparent);
                    qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    bushu.Background = new SolidColorBrush(Colors.Transparent);
                    bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    renshe.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
                    renshe.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
                    shop.Visibility = Visibility.Collapsed;
                    vip.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    vip.Background = new SolidColorBrush(Colors.Transparent);
                    gerenyemian.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                MessageBox.Show("当前不存在网络链接，请链接网络");
                fastbushu.Visibility = Visibility.Collapsed;
                gerenyemian.Visibility = Visibility.Collapsed;
                qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
                qution.Background = new SolidColorBrush(Colors.Transparent); ;
                setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
                setting.Background = new SolidColorBrush(Colors.Transparent);
                qution.Background = new SolidColorBrush(Colors.Transparent);
                qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
                chat.Background = new SolidColorBrush(Colors.Transparent);
                chat.BorderBrush = new SolidColorBrush(Colors.Transparent);
                renshe.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2f2f2f"));
                renshe.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
                fastbushu.Visibility = Visibility.Collapsed;
            }
        }

        private void geren_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            chat.BorderBrush = new SolidColorBrush(Colors.Transparent);
            chat.Background = new SolidColorBrush(Colors.Transparent);
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent); ;
            qution.Background = new SolidColorBrush(Colors.Transparent); ;
            setting.BorderBrush = new SolidColorBrush(Colors.Transparent);
            setting.Background = new SolidColorBrush(Colors.Transparent);
            qution.Background = new SolidColorBrush(Colors.Transparent);
            qution.BorderBrush = new SolidColorBrush(Colors.Transparent);
            bushu.Background = new SolidColorBrush(Colors.Transparent);
            bushu.BorderBrush = new SolidColorBrush(Colors.Transparent);
            gerenyemian.Visibility = Visibility.Visible;
            shop.Visibility = Visibility.Collapsed;
            vip.BorderBrush = new SolidColorBrush(Colors.Transparent);
            vip.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void queding1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {           
            
            opcity04.Visibility = Visibility.Collapsed;
            powerby.Visibility = Visibility.Collapsed;
            NewtonsoftJsonborder.Visibility = Visibility.Collapsed;
            mianzeshangming.Visibility = Visibility.Collapsed;
            ookiidialogsborder.Visibility = Visibility.Collapsed;
        }

        private void NewtonsoftJson_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            NewtonsoftJsonborder.Visibility = Visibility.Visible;
        }


        private void ookiidialogswpf_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ookiidialogsborder.Visibility = Visibility.Visible;
        }
    }
}