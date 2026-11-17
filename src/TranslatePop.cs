// TranslatePop.cs -- 划词翻译悬浮卡片 (Mtranserver 5.1.1)
// 触发: Win+反引号  ->  模拟 Ctrl+C 取词 -> 调 POST /translate -> 鼠标旁浮出译文卡片
// 编译: build.cmd (csc.exe .NET Framework 4.x, C# 5 语法)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TranslatePop
{
    // ---------------- 原生 API ----------------
    internal static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_LSHIFT = 0xA0;
        public const int VK_RSHIFT = 0xA1;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;
        public const int VK_C = 0x43;
        public const int VK_INSERT = 0x2D;

        public const uint KEYEVENTF_KEYUP = 0x0002;

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public int a; public int b; public int c; public int d; public int e; public int f; }

        /// 打印当前前台窗口，用于诊断 Ctrl+C 到底发给了谁
        public static string DescribeForeground()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "null";
            StringBuilder t = new StringBuilder(256);
            GetWindowText(h, t, 256);
            StringBuilder c = new StringBuilder(256);
            GetClassName(h, c, 256);
            uint pid = 0;
            GetWindowThreadProcessId(h, out pid);
            return "hwnd=0x" + h.ToInt64().ToString("X") + " pid=" + pid + " cls=" + c.ToString() + " title=" + t.ToString();
        }

        public static bool KeyDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }

    // ---------------- 轻量 JSON ----------------
    internal static class Json
    {
        public static string Quote(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Unescape(string s)
        {
            if (s == null) return null;
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\') { sb.Append(c); continue; }
                i++;
                if (i >= s.Length) break;
                char e = s[i];
                if (e == 'n') sb.Append('\n');
                else if (e == 'r') sb.Append('\r');
                else if (e == 't') sb.Append('\t');
                else if (e == 'b') sb.Append('\b');
                else if (e == 'f') sb.Append('\f');
                else if (e == 'u')
                {
                    if (i + 4 < s.Length)
                    {
                        int code;
                        if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                    }
                }
                else sb.Append(e);
            }
            return sb.ToString();
        }

        /// 读取 {"key":["a","b"]} 形式的字符串数组；格式不对返回 null。
        public static string[] GetStringArray(string json, string key)
        {
            if (json == null) return null;
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return null;
            int lb = json.IndexOf('[', i);
            if (lb < 0) return null;
            List<string> outp = new List<string>();
            int j = lb + 1;
            while (j < json.Length)
            {
                while (j < json.Length && (char.IsWhiteSpace(json[j]) || json[j] == ',')) j++;
                if (j >= json.Length) return null;
                if (json[j] == ']') break;
                if (json[j] != '"') return null;
                int start = j + 1;
                int k = start;
                bool esc = false;
                while (k < json.Length)
                {
                    char c = json[k];
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') break;
                    k++;
                }
                if (k >= json.Length) return null;
                outp.Add(Unescape(json.Substring(start, k - start)));
                j = k + 1;
            }
            return outp.ToArray();
        }

        public static string GetString(string json, string key)
        {
            if (json == null) return null;
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i + pat.Length);
            if (i < 0) return null;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return null;
            int start = i + 1;
            int j = start;
            bool esc = false;
            while (j < json.Length)
            {
                char c = json[j];
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') break;
                j++;
            }
            if (j >= json.Length) return null;
            return Unescape(json.Substring(start, j - start));
        }
    }

    // ---------------- 配置 ----------------
    internal sealed class Cfg
    {
        public string Endpoint = "http://localhost:8989";
        public string From = "en";
        public string To = "zh";
        public string Hotkey = "alt+" + (char)0x60;
        public string FallbackHotkey = "ctrl+alt+" + (char)0x60;
        public int AutoHideMs = 8000;
        public int MaxWidth = 460;
        public float FontSizePx = 16f;
        public int MaxChars = 3000;

        /// 启动时如何取得管理员权限：
        ///   "auto"（默认）—— 已有最高权限计划任务就走它静默启动（不弹 UAC）；
        ///                    没有任务则退回 runas 提权（会弹一次 UAC）。
        ///   "on"          —— 每次都 runas 提权，每次都弹 UAC。
        ///   "off"         —— 完全不提权，启动不弹任何框，但无法向以管理员运行的程序（如 IDEA）注入按键。
        /// 为什么要提权：UIPI 会挡住低权限进程向高权限窗口发的 SendInput，
        /// 不提权时取词恒为空（grab miss / len=0）。
        public string ElevateMode = "auto";

        /// 代码模式：选中标识符时额外给出逐词对照（getUserById -> get 获取 · user 用户）
        public bool CodeMode = true;

        private static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir != null) return _dir;
                // 优先用 exe 同级的 data\：这样「便携」部署时配置只有一份，
                // 不会出现「普通权限跑读 dist\data、提权跑读 %APPDATA%」两套配置打架
                // （热键、elevate 这些开关必须只有一处生效，否则改了没反应）。
                string local = null;
                try
                {
                    string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(exe)) local = Path.Combine(Path.GetDirectoryName(exe), "data");
                }
                catch { }
                if (local != null && Probe(local)) { _dir = local; return _dir; }
                string app = null;
                try { app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TranslatePop"); } catch { }
                if (app != null && Probe(app)) { _dir = app; return _dir; }
                if (local == null) local = Path.Combine(Path.GetTempPath(), "TranslatePop");
                if (Probe(local)) { _dir = local; return _dir; }
                _dir = Path.GetTempPath();
                return _dir;
            }
        }

        private static bool Probe(string d)
        {
            try
            {
                Directory.CreateDirectory(d);
                string probe = Path.Combine(d, ".probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static string ConfigPath { get { return Path.Combine(Dir, "config.ini"); } }
        public static string LogPath { get { return Path.Combine(Dir, "log.txt"); } }

        public static Cfg Load()
        {
            Cfg c = new Cfg();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                    foreach (string raw in lines)
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "endpoint") c.Endpoint = v;
                        else if (k == "from") c.From = v;
                        else if (k == "to") c.To = v;
                        else if (k == "hotkey") c.Hotkey = v;
                        else if (k == "fallbackhotkey") c.FallbackHotkey = v;
                        else if (k == "autohidems") { int n; if (int.TryParse(v, out n)) c.AutoHideMs = n; }
                        else if (k == "maxwidth") { int n; if (int.TryParse(v, out n)) c.MaxWidth = n; }
                        else if (k == "fontsizepx") { float f; if (float.TryParse(v, out f)) c.FontSizePx = f; }
                        else if (k == "maxchars") { int n; if (int.TryParse(v, out n)) c.MaxChars = n; }
                        else if (k == "codemode") c.CodeMode = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase));
                        else if (k == "elevate") c.ElevateMode = NormalizeMode(v);
                    }
                }
                else
                {
                    c.Save();
                }
            }
            catch (Exception ex) { Log.Write("Cfg.Load: " + ex.Message); }
            if (string.IsNullOrEmpty(c.Endpoint)) c.Endpoint = "http://localhost:8989";
            return c;
        }

        /// 把配置里的 elevate 值归一化成 auto/on/off。
        /// 兼容老写法：1/true/yes → on（老语义就是每次 runas）；
        /// 0/false/no → off；auto 或空 → auto。
        private static string NormalizeMode(string v)
        {
            string s = (v ?? "").Trim().ToLowerInvariant();
            if (s == "auto" || s == "") return "auto";
            if (s == "0" || s == "false" || s == "no" || s == "off") return "off";
            if (s == "1" || s == "true" || s == "yes" || s == "on") return "on";
            return "auto";
        }

        public void Save()
        {
            try
            {
                char bt = (char)0x60;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# TranslatePop 配置（改完重启程序生效）");
                sb.AppendLine("# endpoint = Mtranserver 地址");
                sb.AppendLine("# hotkey   = 触发热键：win/ctrl/alt/shift 组合 + " + bt + " 或 ' 或 a-z 或 f1-f12");
                sb.AppendLine("#            例：alt+" + bt + "   ctrl+alt+" + bt + "   ctrl+alt+t   ctrl+shift+d");
                sb.AppendLine("#            注意：含 win 的组合会吃掉按键，可能触发开始菜单；alt 组合不会拦截按键，更稳");
                sb.AppendLine("# autohidems = 卡片自动消失毫秒数（0 = 不自动消失）");
                sb.AppendLine("# from/to  = 语言方向（默认英译中）");
                sb.AppendLine("# codemode = 1/0：选中代码标识符时是否加一行逐词对照（词表见 data/glossary.txt）");
                sb.AppendLine("# elevate  = auto/on/off：启动时如何取得管理员权限。默认 auto。");
                sb.AppendLine("#            auto = 有「最高权限」计划任务就走它静默启动，不弹 UAC；没有任务才 runas（弹一次 UAC）");
                sb.AppendLine("#            on   = 每次都 runas 提权，每次启动都弹一次 UAC");
                sb.AppendLine("#            off  = 完全不提权，启动零弹窗，但无法向以管理员运行的程序（如 IDEA）注入取词按键");
                sb.AppendLine("endpoint=" + Endpoint);
                sb.AppendLine("from=" + From);
                sb.AppendLine("to=" + To);
                sb.AppendLine("hotkey=" + Hotkey);
                sb.AppendLine("fallbackhotkey=" + FallbackHotkey);
                sb.AppendLine("autohidems=" + AutoHideMs);
                sb.AppendLine("maxwidth=" + MaxWidth);
                sb.AppendLine("fontsizepx=" + FontSizePx);
                sb.AppendLine("maxchars=" + MaxChars);
                sb.AppendLine("codemode=" + (CodeMode ? "1" : "0"));
                sb.AppendLine("elevate=" + ElevateMode);
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("Cfg.Save: " + ex.Message); }
        }
    }

    // ---------------- 日志 ----------------
    internal static class Log
    {
        private static readonly object Gate = new object();
        public static string OverridePath;

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    string p = OverridePath;
                    if (p == null) p = Cfg.LogPath;
                    if (p == null) return;
                    FileInfo fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 256 * 1024) File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }

    // ---------------- 热键解析 ----------------
    internal static class Hotkey
    {
        public static bool TryParse(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string key = null;
            string[] parts = s.ToLowerInvariant().Split('+');
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                if (p == "win" || p == "super" || p == "meta") mods |= Native.MOD_WIN;
                else if (p == "ctrl" || p == "control") mods |= Native.MOD_CONTROL;
                else if (p == "alt") mods |= Native.MOD_ALT;
                else if (p == "shift") mods |= Native.MOD_SHIFT;
                else key = p;
            }
            if (key == null) return false;
            return KeyCode(key, out vk);
        }

        private static bool KeyCode(string k, out uint vk)
        {
            vk = 0;
            if (k.Length == 1 && k[0] == (char)0x60) { vk = 0xC0; return true; }
            if (k == "backquote" || k == "tilde" || k == "oem3" || k == "grave") { vk = 0xC0; return true; }
            if (k == "'" || k == "quote") { vk = 0xDE; return true; }
            if (k.Length == 1)
            {
                char c = k[0];
                if (c >= 'a' && c <= 'z') { vk = (uint)(0x41 + (c - 'a')); return true; }
                if (c >= '0' && c <= '9') { vk = (uint)(0x30 + (c - '0')); return true; }
            }
            if (k.Length >= 2 && k[0] == 'f' && k.Length <= 3)
            {
                int n;
                if (int.TryParse(k.Substring(1), out n) && n >= 1 && n <= 24) { vk = (uint)(0x6F + n); return true; }
            }
            return false;
        }

        public static string Describe(uint mods, uint vk)
        {
            StringBuilder sb = new StringBuilder();
            if ((mods & Native.MOD_WIN) != 0) sb.Append("Win+");
            if ((mods & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mods & Native.MOD_ALT) != 0) sb.Append("Alt+");
            if ((mods & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
            if (vk >= 0x41 && vk <= 0x5A) sb.Append((char)vk);
            else if (vk >= 0x30 && vk <= 0x39) sb.Append((char)vk);
            else if (vk == 0xC0) sb.Append((char)0x60);
            else if (vk == 0xDE) sb.Append("'");
            else if (vk >= 0x70 && vk <= 0x87) sb.Append("F").Append(vk - 0x6F);
            else sb.Append("0x").Append(vk.ToString("X2"));
            return sb.ToString();
        }
    }

    // ---------------- 取词 ----------------
    internal static class Grabber
    {
        private static readonly int[] ModifierKeys = new int[]
        {
            Native.VK_LWIN, Native.VK_RWIN, Native.VK_SHIFT, Native.VK_CONTROL, Native.VK_MENU,
            Native.VK_LSHIFT, Native.VK_RSHIFT, Native.VK_LCONTROL, Native.VK_RCONTROL, Native.VK_LMENU, Native.VK_RMENU
        };

        public static bool WaitModifiersReleased(int timeoutMs)
        {
            DateTime end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                bool any = false;
                for (int i = 0; i < ModifierKeys.Length; i++)
                {
                    if (Native.KeyDown(ModifierKeys[i])) { any = true; break; }
                }
                if (!any) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        private static void SendKeyVk(int vk, bool scan)
        {
            const uint KEYUP = 0x0002;
            const uint SCAN = 0x0008;
            uint extra = scan ? SCAN : 0;
            uint bScan = Native.MapVirtualKey((uint)vk, 0);
            Native.INPUT kd = new Native.INPUT();
            kd.type = 1;
            kd.a = vk;
            kd.b = (int)bScan;
            kd.c = (int)extra;
            Native.SendInput(1, new Native.INPUT[] { kd }, 28);
            Thread.Sleep(25);
            Native.INPUT ku = new Native.INPUT();
            ku.type = 1;
            ku.a = vk;
            ku.b = (int)bScan;
            ku.c = (int)(KEYUP | extra);
            Native.SendInput(1, new Native.INPUT[] { ku }, 28);
            Thread.Sleep(15);
        }

        /// kind 0 = SendInput 扫描码 Ctrl+C；1 = keybd_event Ctrl+C；2 = Ctrl+Insert；3 = Shift+Ctrl+C 后 Ctrl+C
        public static void SendCopyVariant(int kind)
        {
            uint up = Native.KEYEVENTF_KEYUP;
            if (kind == 1)
            {
                Native.keybd_event((byte)Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
                Thread.Sleep(20);
                Native.keybd_event((byte)Native.VK_C, 0, 0, UIntPtr.Zero);
                Thread.Sleep(22);
                Native.keybd_event((byte)Native.VK_C, 0, up, UIntPtr.Zero);
                Thread.Sleep(20);
                Native.keybd_event((byte)Native.VK_CONTROL, 0, up, UIntPtr.Zero);
                return;
            }
            int ctrl = Native.VK_CONTROL;
            int key = (kind == 2) ? Native.VK_INSERT : Native.VK_C;
            const uint KEYUP = 0x0002;
            const uint SCAN = 0x0008;
            Native.INPUT[] seq = new Native.INPUT[kind == 3 ? 6 : 4];
            int n = 0;
            Native.INPUT a = new Native.INPUT(); a.type = 1; a.a = ctrl; a.b = (int)Native.MapVirtualKey((uint)ctrl, 0); a.c = (int)SCAN;
            seq[n++] = a;
            if (kind == 3)
            {
                Native.INPUT sh = new Native.INPUT(); sh.type = 1; sh.a = Native.VK_SHIFT; sh.b = (int)Native.MapVirtualKey((uint)Native.VK_SHIFT, 0); sh.c = (int)SCAN;
                seq[n++] = sh;
                Native.INPUT shu = new Native.INPUT(); shu.type = 1; shu.a = Native.VK_SHIFT; shu.b = (int)Native.MapVirtualKey((uint)Native.VK_SHIFT, 0); shu.c = (int)(KEYUP | SCAN);
                Native.INPUT ck = new Native.INPUT(); ck.type = 1; ck.a = key; ck.b = (int)Native.MapVirtualKey((uint)key, 0); ck.c = (int)SCAN;
                Native.INPUT cku = new Native.INPUT(); cku.type = 1; cku.a = key; cku.b = (int)Native.MapVirtualKey((uint)key, 0); cku.c = (int)(KEYUP | SCAN);
                Native.INPUT[] mini = new Native.INPUT[] { sh, ck, cku, shu };
                Native.SendInput(4, mini, 28);
                Thread.Sleep(60);
                n = 1;
            }
            Native.INPUT b = new Native.INPUT(); b.type = 1; b.a = key; b.b = (int)Native.MapVirtualKey((uint)key, 0); b.c = (int)SCAN;
            Native.INPUT c = new Native.INPUT(); c.type = 1; c.a = key; c.b = (int)Native.MapVirtualKey((uint)key, 0); c.c = (int)(KEYUP | SCAN);
            Native.INPUT d = new Native.INPUT(); d.type = 1; d.a = ctrl; d.b = (int)Native.MapVirtualKey((uint)ctrl, 0); d.c = (int)(KEYUP | SCAN);
            seq[n++] = b; seq[n++] = c; seq[n++] = d;
            Native.SendInput((uint)n, seq, 28);
        }

        public static void SendCopy() { SendCopyVariant(0); }

        private static DataObject Snapshot()
        {
            IDataObject src = Clipboard.GetDataObject();
            if (src == null) return null;
            DataObject copy = new DataObject();
            string[] formats = src.GetFormats();
            int kept = 0;
            foreach (string f in formats)
            {
                if (!IsCheapFormat(f)) continue;
                try
                {
                    object d = src.GetData(f);
                    if (d != null) { copy.SetData(f, d); kept++; }
                }
                catch { }
            }
            return kept > 0 ? copy : null;
        }

        /// 只备份轻量格式，避免大对象（图片/文件流）拷贝卡住几百毫秒到几秒。
        private static bool IsCheapFormat(string f)
        {
            if (string.IsNullOrEmpty(f)) return false;
            return string.Equals(f, DataFormats.UnicodeText, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, DataFormats.Text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, DataFormats.Html, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, DataFormats.Rtf, StringComparison.OrdinalIgnoreCase)
                || f.Equals("HTML Format", StringComparison.OrdinalIgnoreCase);
        }

        private static void Restore(IDataObject snapshot)
        {
            if (snapshot == null) return;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(snapshot, true);
                    return;
                }
                catch { Thread.Sleep(60); }
            }
        }

        private static string TryReadClipboard(uint before, int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(20);
                try
                {
                    if (Native.GetClipboardSequenceNumber() == before) continue;
                    if (!Clipboard.ContainsText()) continue;
                    string t = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(t)) return t;
                }
                catch { }
            }
            return "";
        }

        /// 模拟 Ctrl+C 读取当前选中文本；多种发键方式依次尝试，返回空字符串表示没取到。
        public static string ReadSelection(out bool restored, out int elapsedMs)
        {
            restored = false;
            elapsedMs = 0;
            IDataObject backup = null;
            try { backup = Snapshot(); } catch { }
            uint before = Native.GetClipboardSequenceNumber();
            DateTime t0 = DateTime.Now;
            string fg = Native.DescribeForeground();
            Log.Write("fg=" + fg);

            string text = "";
            int[] order = new int[] { 1, 0, 2, 3 };
            for (int i = 0; i < order.Length && text.Length == 0; i++)
            {
                int kind = order[i];
                SendCopyVariant(kind);
                text = TryReadClipboard(before, i == 0 ? 500 : 450);
                if (text.Length > 0)
                {
                    Log.Write("grab ok variant=" + kind);
                    break;
                }
                Log.Write("grab miss variant=" + kind);
                before = Native.GetClipboardSequenceNumber();
            }
            elapsedMs = (int)(DateTime.Now - t0).TotalMilliseconds;

            if (text.Length > 0)
            {
                restored = backup != null;
                if (backup != null)
                {
                    IDataObject snap = backup;
                    ThreadPool.QueueUserWorkItem(delegate (object st)
                    {
                        try { Thread.Sleep(80); Restore((IDataObject)st); }
                        catch { }
                    }, snap);
                }
            }

            text = text.Replace("\r\n", "\n").Trim();
            return text;
        }
    }

    // ---------------- 翻译 ----------------
    internal static class Api
    {
        public static string Translate(Cfg cfg, string text, out string error, out int ms)
        {
            error = null;
            ms = 0;
            DateTime t0 = DateTime.Now;
            try
            {
                string url = cfg.Endpoint.TrimEnd('/') + "/translate";
                string body = "{\"from\":" + Json.Quote(cfg.From) + ",\"to\":" + Json.Quote(cfg.To) + ",\"text\":" + Json.Quote(text) + "}";
                byte[] buf = Encoding.UTF8.GetBytes(body);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;
                req.Proxy = null;
                req.KeepAlive = false;
                using (Stream s = req.GetRequestStream()) s.Write(buf, 0, buf.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string raw = sr.ReadToEnd();
                    ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                    string result = Json.GetString(raw, "result");
                    if (result == null)
                    {
                        error = "返回格式异常：" + Clip(raw, 120);
                        return null;
                    }
                    return result;
                }
            }
            catch (WebException we)
            {
                ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                error = DescribeWebError(we, cfg.Endpoint);
                return null;
            }
            catch (Exception ex)
            {
                ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                error = ex.Message;
                return null;
            }
        }

        /// 批量翻译：一次往返同时拿到整句与各词（本机 Mtranserver 内部串行，并发反而更慢）。
        public static string[] TranslateBatch(Cfg cfg, string[] texts, out string error, out int ms)
        {
            error = null;
            ms = 0;
            DateTime t0 = DateTime.Now;
            try
            {
                string url = cfg.Endpoint.TrimEnd('/') + "/translate/batch";
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"from\":" + Json.Quote(cfg.From) + ",\"to\":" + Json.Quote(cfg.To) + ",\"texts\":[");
                for (int i = 0; i < texts.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Json.Quote(texts[i] == null ? "" : texts[i]));
                }
                sb.Append("]}");
                byte[] buf = Encoding.UTF8.GetBytes(sb.ToString());
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;
                req.Proxy = null;
                req.KeepAlive = false;
                using (Stream s = req.GetRequestStream()) s.Write(buf, 0, buf.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string raw = sr.ReadToEnd();
                    ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                    string[] arr = Json.GetStringArray(raw, "results");
                    if (arr == null)
                    {
                        error = "返回格式异常：" + Clip(raw, 120);
                        return null;
                    }
                    return arr;
                }
            }
            catch (WebException we)
            {
                ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                error = DescribeWebError(we, cfg.Endpoint);
                return null;
            }
            catch (Exception ex)
            {
                ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                error = ex.Message;
                return null;
            }
        }

        private static string DescribeWebError(WebException we, string endpoint)
        {
            if (we.Status == WebExceptionStatus.ConnectFailure || we.Status == WebExceptionStatus.Timeout || we.Status == WebExceptionStatus.NameResolutionFailure)
                return "连不上翻译服务 " + endpoint + "（Mtranserver 是否在运行？）";
            HttpWebResponse r = we.Response as HttpWebResponse;
            if (r != null)
            {
                string body = "";
                try
                {
                    using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
                }
                catch { }
                return "HTTP " + (int)r.StatusCode + " " + Clip(body, 140);
            }
            return we.Message;
        }

        public static string Get(Cfg cfg, string path)
        {
            string url = cfg.Endpoint.TrimEnd('/') + path;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 8000;
            req.Proxy = null;
            req.KeepAlive = false;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static string Clip(string s, int n)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }
    }

    // ---------------- 代码模式：拆词 + 内置术语表 ----------------
    internal static class CodeText
    {
        /// 拆词：camelCase / PascalCase / SNAKE_CASE / kebab-case / 标点分隔
        /// 前两段来自 IDEA 插件常用写法，第三段把非字母数字一律当分隔符。
        private static readonly Regex Splitter = new Regex(
            "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|[^A-Za-z0-9]+",
            RegexOptions.Compiled);

        private static Dictionary<string, string> _glossary;
        private static readonly object Gate = new object();

        public static string[] Split(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            string[] raw = Splitter.Split(s);
            List<string> outp = new List<string>();
            foreach (string w in raw)
            {
                if (w == null) continue;
                string t = w.Trim();
                if (t.Length == 0) continue;
                outp.Add(t);
            }
            return outp.ToArray();
        }

        /// 句子里是否夹着标识符（UserService / user_id / HTTP2Server），
        /// 普通英文句子基本不会出现，出现就值得先摊开再翻译。
        public static bool HasIdentifiers(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_') { i++; continue; }
                int j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                string tok = s.Substring(i, j - i);
                if (tok.IndexOf('_') >= 0) return true;
                for (int k = 1; k < tok.Length; k++)
                {
                    if (char.IsLower(tok[k - 1]) && char.IsUpper(tok[k])) return true;
                }
                i = j;
            }
            return false;
        }

        /// 判据：像标识符/代码片段，才值得拆词并给逐词对照。
        public static bool LooksLikeCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Length > 200) return false;
            bool space = false;
            for (int i = 0; i < s.Length; i++) { if (char.IsWhiteSpace(s[i])) { space = true; break; } }

            // 强代码标点：有就是代码（括号/花括号/分号/等号/下划线等），不论有无空格
            bool strong = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(' || c == ')' || c == '{' || c == '}' || c == '[' || c == ']'
                    || c == '=' || c == '<' || c == '>' || c == ';' || c == '_'
                    || c == '"' || c == '@' || c == '$' || c == '\\') { strong = true; break; }
            }
            if (strong) return true;

            // 弱代码标点：只有当一个词里出现时才算（句号/斜杠/井号/冒号在英文句子里很常见）
            if (!space)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '.' || c == '/' || c == '#' || c == ':') return true;
                }
            }
            if (space) return false;   // 有空格又没强代码标点 => 当普通短语/整句
            // 单词内的 camelCase / PascalCase / 全大写缩写
            for (int i = 1; i < s.Length; i++)
            {
                if (char.IsLower(s[i - 1]) && char.IsUpper(s[i])) return true;
            }
            if (s.Length >= 3)
            {
                bool allUpper = true;
                for (int i = 0; i < s.Length; i++) { if (!char.IsUpper(s[i])) { allUpper = false; break; } }
                if (allUpper) return true;
            }
            return false;
        }

        public static bool TryGloss(string word, out string zh)
        {
            zh = null;
            if (word == null) return false;
            Dictionary<string, string> g = Glossary;
            if (g.Count == 0) return false;
            if (g.TryGetValue(word, out zh)) return true;
            // 复数/三单顺手降级：handles -> handle、classes -> class
            if (word.Length > 3 && word[word.Length - 1] == 's')
            {
                string stem = word.Substring(0, word.Length - 1);
                if (g.TryGetValue(stem, out zh)) return true;
                if (stem.Length > 2 && stem[stem.Length - 1] == 'e')
                {
                    if (g.TryGetValue(stem.Substring(0, stem.Length - 1), out zh)) return true;
                }
            }
            return false;
        }

        public static Dictionary<string, string> Glossary
        {
            get
            {
                if (_glossary != null) return _glossary;
                lock (Gate)
                {
                    if (_glossary != null) return _glossary;
                    Dictionary<string, string> g = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string pair in Builtin.Split('|'))
                    {
                        if (pair.Length == 0) continue;
                        int sp = pair.IndexOf(' ');
                        if (sp <= 0) continue;
                        string k = pair.Substring(0, sp).Trim();
                        string v = pair.Substring(sp + 1).Trim();
                        if (k.Length > 0 && v.Length > 0) g[k] = v;
                    }
                    // 用户可覆盖/追加：data/glossary.txt 每行「word=中文」或「word 中文」，# 开头为注释
                    try
                    {
                        string p = Path.Combine(Cfg.Dir, "glossary.txt");
                        if (File.Exists(p))
                        {
                            foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
                            {
                                string line = raw.Trim();
                                if (line.Length == 0 || line.StartsWith("#")) continue;
                                int sep = line.IndexOf('=');
                                if (sep < 0) sep = line.IndexOf('\t');
                                if (sep < 0) sep = line.IndexOf(' ');
                                if (sep <= 0) continue;
                                string k = line.Substring(0, sep).Trim();
                                string v = line.Substring(sep + 1).Trim();
                                if (k.Length > 0 && v.Length > 0) g[k] = v;
                            }
                        }
                        else
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("# 自定义术语表：每行 word=中文，# 开头为注释；与内置词表同名则覆盖。");
                            sb.AppendLine("# 例： handler=处理器");
                            File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
                        }
                    }
                    catch { }
                    _glossary = g;
                    return _glossary;
                }
            }
        }

        private const string Builtin =
            "abstract 抽象|add 添加|all 全部|alloc 分配|allocator 分配器|annotation 注解|anonymous 匿名|api 接口|app 应用|application 应用|arg 参数|args 参数|argument 参数|array 数组|async 异步|await 等待|base 基类|batch 批处理|binary 二进制|bind 绑定|block 阻塞|bool 布尔|boolean 布尔|break 跳出|buf 缓冲|buffer 缓冲区|build 构建|by 按|byte 字节|cache 缓存|call 调用|callback 回调|cancel 取消|case 分支|catch 捕获|cell 单元格|char 字符|check 检查|child 子项|class 类|clear 清空|click 点击|client 客户端|clone 克隆|close 关闭|closure 闭包|code 代码|collection 集合|snakecase 蛇形命名|snake 蛇形命名|command 命令|comment 注释|commit 提交|compare 比较|compile 编译|component 组件|config 配置|configuration 配置|connection 连接|conn 连接|const 常量|constant 常量|constructor 构造函数|container 容器|continue 继续|copy 复制|count 计数|create 创建|ctx 上下文|current 当前|cursor 光标|data 数据|database 数据库|db 数据库|debug 调试|decode 解码|default 默认|defer 延迟|del 删除|delete 删除|dependency 依赖|deploy 部署|deserialize 反序列化|destroy 销毁|device 设备|dict 字典|dictionary 字典|dir 目录|directory 目录|disable 禁用|dispose 释放|doc 文档|document 文档|domain 域|double 双精度|download 下载|driver 驱动|dump 转储|dynamic 动态|edit 编辑|element 元素|else 否则|empty 空|enable 启用|encode 编码|end 结束|entity 实体|entry 条目|enum 枚举|env 环境|environment 环境|err 错误|error 错误|event 事件|exception 异常|exec 执行|execute 执行|executor 执行器|exit 退出|export 导出|expr 表达式|expression 表达式|extend 扩展|extension 扩展|false 假|fetch 获取|field 字段|file 文件|filter 过滤|final 最终|finally 最终|find 查找|flag 标志|float 浮点|flush 刷新|fn 函数|folder 文件夹|font 字体|for 循环|foreach 遍历|fork 分叉|format 格式化|frame 帧|from 从|func 函数|function 函数|future 将来|generic 泛型|get 获取|global 全局|handle 处理|handler 处理器|hash 哈希|header 请求头|heap 堆|helper 辅助|hidden 隐藏|hide 隐藏|hook 钩子|host 主机|html 网页|http 网络|icon 图标|id ID|identifier 标识符|if 如果|implements 实现|impl 实现类|img 图像|image 图像|import 导入|index 索引|info 信息|init 初始化|initialize 初始化|inline 内联|input 输入|insert 插入|instance 实例|int 整数|integer 整数|interface 接口|invalid 无效|invoke 调用|is 是否|item 项|iter 迭代器|iterate 迭代|iterator 迭代器|job 任务|join 连接|json 数据格式|key 键|keyword 关键字|lambda 匿名函数|lang 语言|language 语言|lazy 惰性|len 长度|length 长度|level 级别|lib 库|library 库|line 行|link 链接|list 列表|listen 监听|load 加载|loader 加载器|local 本地|lock 锁|log 日志|logger 日志器|loop 循环|map 映射|mapping 映射|match 匹配|max 最大|member 成员|mem 内存|memory 内存|menu 菜单|merge 合并|msg 消息|message 消息|method 方法|middleware 中间件|min 最小|mock 模拟|mode 模式|model 模型|module 模块|move 移动|mutex 互斥锁|name 名称|namespace 命名空间|native 本机|new 新建|next 下一个|node 节点|none 无|null 空值|num 数字|number 数字|obj 对象|object 对象|offset 偏移|open 打开|operator 运算符|opt 选项|option 选项|order 顺序|out 输出|output 输出|override 重写|package 包|page 页面|pair 对|param 参数|parameter 参数|parent 父项|parse 解析|parser 解析器|path 路径|pattern 模式|payload 载荷|peer 对端|pending 挂起|permission 权限|pid 进程号|pipe 管道|pipeline 流水线|pixel 像素|plugin 插件|pointer 指针|pool 池|pop 弹出|port 端口|pos 位置|position 位置|post 提交|prefix 前缀|prev 上一个|previous 上一个|print 打印|priority 优先级|private 私有|proc 进程|process 进程|processor 处理器|program 程序|project 项目|promise 约定|prop 属性|property 属性|protected 受保护|protocol 协议|proxy 代理|pub 发布|public 公有|pull 拉取|push 推送|query 查询|queue 队列|quit 退出|range 范围|read 读取|reader 读取器|recv 接收|receive 接收|record 记录|reduce 归约|ref 引用|reference 引用|register 注册|registry 注册表|remove 移除|render 渲染|repeat 重复|req 请求|request 请求|require 引入|reset 重置|res 响应|resolve 解析|resource 资源|resp 响应|response 响应|result 结果|ret 返回|return 返回|retry 重试|root 根|route 路由|router 路由器|run 运行|runner 运行器|runtime 运行时|save 保存|scan 扫描|scheduler 调度器|scope 作用域|script 脚本|search 搜索|select 选择|selector 选择器|send 发送|sender 发送方|server 服务器|service 服务|session 会话|set 设置|setting 设置|setup 初始化|shared 共享|shift 位移|show 显示|signal 信号|size 大小|skip 跳过|slice 切片|socket 套接字|sort 排序|src 源|source 源|split 切分|sql 数据库查询|stack 栈|start 开始|state 状态|static 静态|status 状态|step 步骤|stop 停止|storage 存储|store 存储|str 字符串|stream 流|string 字符串|struct 结构体|style 样式|sub 子项|submit 提交|subscribe 订阅|success 成功|super 父类|switch 开关|sync 同步|sys 系统|system 系统|tag 标签|target 目标|task 任务|temp 临时|template 模板|test 测试|text 文本|thread 线程|throw 抛出|time 时间|timeout 超时|timer 定时器|token 令牌|tool 工具|trace 追踪|tree 树|true 真|try 尝试|type 类型|ui 界面|unit 单元|update 更新|upload 上传|url 地址|use 使用|user 用户|util 工具|utility 工具|valid 有效|validate 校验|val 值|value 值|var 变量|variable 变量|vector 向量|verbose 详细|version 版本|view 视图|virtual 虚拟|void 空|wait 等待|warn 警告|warning 警告|watch 监听|web 网络|while 循环|widget 控件|width 宽度|win 窗口|window 窗口|worker 工作线程|wrapper 包装器|write 写入|writer 写入器|xml 数据格式|yield 让出|zip 压缩|zone 区域";

        /// 只有一个标识符、没有空格也没有强代码标点（getUserById / SNAKE_CASE_CONST 之外的单驼峰值）
        public static bool IsSingleIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }

        /// 是否含强代码标点（括号/分号/等号之类）
        public static bool HasCodePunct(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(' || c == ')' || c == '{' || c == '}' || c == '[' || c == ']'
                    || c == '=' || c == ';' || c == '<' || c == '>' || c == '_') return true;
            }
            return false;
        }

        /// 拉丁字母占非空白字符的比例，判断"这行到底译没译"
        public static float LatinRatio(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            int latin = 0;
            int total = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) continue;
                total++;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            }
            if (total == 0) return 0f;
            return (float)latin / (float)total;
        }

        /// 译文里有没有汉字（用来判断到底翻没翻出来）
        public static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            }
            return false;
        }

        /// 译文里残留的英文单词，能查到词表的就换成中文（Impl -> 实现类）。
        public static string PatchLeftovers(string zh)
        {
            if (string.IsNullOrEmpty(zh)) return zh;
            StringBuilder sb = new StringBuilder(zh.Length + 16);
            int i = 0;
            while (i < zh.Length)
            {
                char c = zh[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    int j = i;
                    while (j < zh.Length && ((zh[j] >= 'a' && zh[j] <= 'z') || (zh[j] >= 'A' && zh[j] <= 'Z')
                        || (zh[j] >= '0' && zh[j] <= '9'))) j++;
                    string tok = zh.Substring(i, j - i);
                    string g;
                    if (tok.Length >= 2 && !IsAllCapsShort(tok) && TryGloss(tok, out g)) sb.Append(g);
                    else sb.Append(tok);
                    i = j;
                }
                else { sb.Append(c); i++; }
            }
            return TidySpaces(sb.ToString());
        }

        /// ID / URL / HTTP 这类全大写缩写保持原样，不硬翻
        private static bool IsAllCapsShort(string s)
        {
            if (s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++) { if (!char.IsUpper(s[i])) return false; }
            return true;
        }

        /// 把句子里夹着的驼峰/下划线标识符就地摊开：UserService -> User Service。
        /// 整句翻译前必须做这一步，否则 Mtranserver 会把 UserService 原样留在译文里。
        public static string Desugar(string s, out List<string> idents)
        {
            idents = new List<string>();
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder(s.Length + 24);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    int j = i;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                    string tok = s.Substring(i, j - i);
                    string[] ps = Split(tok);
                    bool isIdent = ps.Length > 1 || tok.IndexOf('_') >= 0;
                    if (isIdent)
                    {
                        sb.Append(string.Join(" ", ps));
                        bool dup = false;
                        foreach (string x in idents) { if (string.Equals(x, tok, StringComparison.Ordinal)) { dup = true; break; } }
                        if (!dup) idents.Add(tok);
                    }
                    else sb.Append(tok);
                    i = j;
                }
                else { sb.Append(c); i++; }
            }
            return sb.ToString();
        }

        /// 标识符整体用词表拼成中文；只要有一个词没收录就返回 false。
        public static bool GlossJoin(string ident, out string zh)
        {
            zh = null;
            string[] ps = Split(ident);
            if (ps.Length == 0) return false;
            StringBuilder sb = new StringBuilder();
            foreach (string p in ps)
            {
                string g;
                if (!TryGloss(p, out g)) return false;
                sb.Append(g);
            }
            if (sb.Length == 0) return false;
            zh = sb.ToString();
            return true;
        }

        /// 把译文里残留的标识符换成中文（getUserById -> 获取用户按ID），并顺手收拾多余空格。
        public static string PatchIdentifiers(string zh, List<string> idents)
        {
            if (string.IsNullOrEmpty(zh) || idents == null || idents.Count == 0) return zh;
            string outp = zh;
            foreach (string ident in idents)
            {
                if (ident == null || ident.Length < 3) continue;
                if (outp.IndexOf(ident, StringComparison.Ordinal) < 0) continue;
                string g;
                if (!GlossJoin(ident, out g)) continue;
                outp = outp.Replace(ident, g);
            }
            return TidySpaces(outp);
        }

        /// 收拾空格：连续空格合一，汉字之间的空格删掉。
        public static string TidySpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ')
                {
                    bool prevCjk = sb.Length > 0 && IsCjk(sb[sb.Length - 1]);
                    int k = i + 1;
                    while (k < s.Length && s[k] == ' ') k++;
                    bool nextCjk = k < s.Length && IsCjk(s[k]);
                    if (prevCjk && nextCjk) { i = k - 1; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool IsCjk(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFF00 && c <= 0xFFEF);
        }

        /// 按原文骨架逐词替换：认识的词用词表译文，不认识的保留原样（标点、空格、括号位置不变）。
        /// 用于 Mtranserver 把整行代码原样吐回来的场景。
        public static string MixLine(string s, out int replaced)
        {
            replaced = 0;
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder(s.Length + 16);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c))
                {
                    int j = i;
                    while (j < s.Length && char.IsLetterOrDigit(s[j])) j++;
                    string tok = s.Substring(i, j - i);
                    string zh;
                    if (tok.Length > 0 && TryGloss(tok, out zh))
                    {
                        sb.Append(zh);
                        replaced++;
                    }
                    else if (tok.Length > 1 && tok.IndexOf('_') < 0)
                    {
                        // 不认识的长词再拆一层驼峰：getUserById -> 获取用户ID
                        string[] ps = Split(tok);
                        if (ps.Length > 1)
                        {
                            StringBuilder nest = new StringBuilder();
                            int hit = 0;
                            foreach (string p in ps)
                            {
                                string g;
                                if (TryGloss(p, out g)) { nest.Append(g); hit++; }
                                else nest.Append(p);
                            }
                            if (hit > 0) { sb.Append(nest.ToString()); replaced += hit; }
                            else sb.Append(tok);
                        }
                        else sb.Append(tok);
                    }
                    else sb.Append(tok);
                    i = j;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            // 保留原文的词间距（中文词之间留空格更好读），只把连续空格压成一个
            string outp = sb.ToString();
            while (outp.IndexOf("  ") >= 0) outp = outp.Replace("  ", " ");
            return outp;
        }

        /// 组装逐词对照行：「get 获取 · user 用户 · by 由」
        public static string BuildSub(string[] words, string[] zh)
        {
            StringBuilder sb = new StringBuilder();
            int n = 0;
            for (int i = 0; i < words.Length && n < 14; i++)
            {
                string w = words[i];
                string z = (i < zh.Length && zh[i] != null) ? zh[i].Trim() : "";
                if (z.Length == 0) continue;
                if (n > 0) sb.Append(" · ");
                sb.Append(w).Append(' ').Append(z);
                n++;
            }
            if (words.Length > 14) sb.Append(" · ...");
            return sb.ToString();
        }
    }

    // ---------------- 悬浮卡片 ----------------
    internal sealed class PopupForm : Form
    {
        private string _text;
        private string _sub;
        private bool _isError;
        private readonly Font _font;
        private readonly Font _subFont;
        private static readonly Color SubColor = Color.FromArgb(148, 156, 174);
        private int _mainH;
        private int _subH;
        private int _gap;
        private readonly int _padX;
        private readonly int _padY;
        private readonly int _radius;
        private static readonly Color BgColor = Color.FromArgb(30, 34, 44);
        private static readonly Color BorderColor = Color.FromArgb(62, 72, 92);
        private static readonly Color BorderErr = Color.FromArgb(150, 70, 70);
        private static readonly Color FgColor = Color.FromArgb(232, 236, 244);
        private static readonly Color FgErr = Color.FromArgb(255, 138, 138);
        private readonly System.Windows.Forms.Timer _timer;
        private readonly int _autoHideMs;
        private DateTime _shownAt = DateTime.Now;
        private string _flash;
        private DateTime _flashUntil = DateTime.MinValue;
        private bool _paused;
        private float _scale = 1f;
        private int _maxW;
        private Point _anchor;
        private bool _pinned;   // 固定位置（自检/演示用），不跟随鼠标

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        public PopupForm(string text, bool isError, Cfg cfg, Point? fixedAt)
        {
            _text = text == null ? "" : text;
            _isError = isError;

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = BgColor;
            DoubleBuffered = true;
            Padding = new Padding(0);

            IntPtr force = this.Handle;

            float scale = 1f;
            using (Graphics g = CreateGraphics())
            {
                scale = g.DpiX / 96f;
                if (scale <= 0f) scale = 1f;
                _padX = (int)Math.Round(16 * scale);
                _padY = (int)Math.Round(13 * scale);
                _radius = (int)Math.Round(10 * scale);

                _scale = scale;
                _maxW = (int)Math.Round(cfg.MaxWidth * scale);
                float fontPx = cfg.FontSizePx * scale;
                _font = new Font("Microsoft YaHei UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
                if (_font.Name != "Microsoft YaHei UI")
                {
                    _font.Dispose();
                    _font = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
                }
                _subFont = new Font(_font.FontFamily, fontPx * 0.86f, FontStyle.Regular, GraphicsUnit.Pixel);
            }

            if (fixedAt.HasValue) { _anchor = fixedAt.Value; _pinned = true; }
            else
            {
                Native.POINT pt;
                Native.GetCursorPos(out pt);
                _anchor = new Point(pt.X, pt.Y);
            }

            ApplyContent();

            _autoHideMs = cfg.AutoHideMs;
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 120;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        /// 重新按当前文本量算尺寸并摆回光标旁（文本会变：先“翻译中…”，再出译文）。
        private void ApplyContent()
        {
            using (Graphics g = CreateGraphics())
            {
                const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
                Size measured = TextRenderer.MeasureText(g, ShownText(), _font, new Size(_maxW, int.MaxValue), flags);
                Size subSz = Size.Empty;
                if (_sub != null && _sub.Length > 0)
                    subSz = TextRenderer.MeasureText(g, ShownSub(), _subFont, new Size(_maxW, int.MaxValue), flags);
                _mainH = measured.Height;
                _subH = subSz.Height;
                _gap = _subH > 0 ? (int)Math.Round(3 * _scale) : 0;

                int contentW = Math.Max(measured.Width, subSz.Width);
                int w = contentW + _padX * 2;
                if (w > _maxW + _padX * 2) w = _maxW + _padX * 2;
                int h = _mainH + _gap + _subH + _padY * 2;
                int maxH = (int)Math.Round(Screen.FromPoint(_anchor).WorkingArea.Height * 0.6);
                if (h > maxH) h = maxH;
                if (w < _padX * 4) w = _padX * 4;
                ClientSize = new Size(w, h);
            }
            PlaceWindow();
            UpdateRegion();
            Invalidate();
        }

        private void PlaceWindow()
        {
            Rectangle wa = Screen.FromPoint(_anchor).WorkingArea;
            int x = _anchor.X + (int)Math.Round(16 * _scale);
            int y = _anchor.Y + (int)Math.Round(22 * _scale);
            if (x + Width > wa.Right - 8) x = Math.Max(wa.Left + 8, wa.Right - Width - 8);
            if (y + Height > wa.Bottom - 8) y = Math.Max(wa.Top + 8, _anchor.Y - Height - (int)Math.Round(18 * _scale));
            Location = new Point(x, y);
        }

        /// 结果回来时原地换文本，不再重建窗口（避免闪烁和二次定位）。
        public void SetText(string text, bool isError)
        {
            SetText(text, null, isError);
        }

        /// 把卡片重新挪到当前鼠标位置（每次新翻译都跟手，不留在上一处）。
        public void AnchorToCursor()
        {
            if (IsDisposed || _pinned) return;
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            Point p = new Point(pt.X, pt.Y);
            if (p == _anchor) return;
            _anchor = p;
            PlaceWindow();
        }

        /// 第二行（逐词对照），传 null 或空串则只显示一行。
        public void SetText(string text, string sub, bool isError)
        {
            if (IsDisposed) return;
            _text = text == null ? "" : text;
            _sub = sub;
            _isError = isError;
            _shownAt = DateTime.Now;
            ApplyContent();
        }

        private string ShownText()
        {
            if (_text.Length > 900) return _text.Substring(0, 900) + " ...";
            return _text;
        }

        private string ShownSub()
        {
            if (_sub == null) return "";
            if (_sub.Length > 600) return _sub.Substring(0, 600) + " ...";
            return _sub;
        }

        private void UpdateRegion()
        {
            try
            {
                using (GraphicsPath p = RoundPath(new Rectangle(0, 0, Width, Height), _radius))
                {
                    Region old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        private static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || r.Width <= d || r.Height <= d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_flash != null && DateTime.Now > _flashUntil)
            {
                _flash = null;
                Invalidate();
            }
            if (_autoHideMs > 0 && !_paused && DateTime.Now.Subtract(_shownAt).TotalMilliseconds > _autoHideMs)
            {
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundPath(r, _radius))
            using (SolidBrush b = new SolidBrush(BgColor))
            {
                g.FillPath(b, path);
            }

            using (GraphicsPath path = RoundPath(new Rectangle(1, 1, Width - 3, Height - 3), Math.Max(2, _radius - 1)))
            using (Pen pen = new Pen(_isError ? BorderErr : BorderColor, 1f))
            {
                g.DrawPath(pen, path);
            }

            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
            int availW = Width - _padX * 2;
            int mainH = (_subH > 0 && _mainH > 0) ? _mainH : Height - _padY * 2;
            TextRenderer.DrawText(g, ShownText(), _font,
                new Rectangle(_padX, _padY, availW, mainH),
                _isError ? FgErr : FgColor, flags);
            if (_subH > 0)
            {
                TextRenderer.DrawText(g, ShownSub(), _subFont,
                    new Rectangle(_padX, _padY + _mainH + _gap, availW, _subH + 2),
                    _isError ? Color.FromArgb(210, 150, 150) : SubColor, flags);
            }

            if (_flash != null)
            {
                Size sz = TextRenderer.MeasureText(g, _flash, _font);
                Rectangle fr = new Rectangle(Width - sz.Width - _padX, Height - sz.Height - 4, sz.Width, sz.Height);
                TextRenderer.DrawText(g, _flash, _font, fr, Color.FromArgb(130, 220, 160));
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _paused = true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _paused = false;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left)
            {
                try
                {
                    Clipboard.SetText(_text);
                    _flash = "已复制";
                    _flashUntil = DateTime.Now.AddMilliseconds(900);
                    Invalidate();
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                    t.Interval = 700;
                    t.Tick += delegate (object s, EventArgs ev)
                    {
                        t.Stop();
                        t.Dispose();
                        Close();
                    };
                    t.Start();
                }
                catch
                {
                    Close();
                }
            }
            else
            {
                Close();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            try { _font.Dispose(); } catch { }
            try { _subFont.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }

    // ---------------- 开机自启（计划任务，最高权限，不弹 UAC） ----------------
    internal static class Autostart
    {
        internal const string TaskName = "TranslatePop";

        /// 「静默提权」专用任务：和「开机自启」分开，避免勾选自启才免 UAC、或不勾自启时
        /// 硬塞一个登录启动项。这个任务只用来被 /Run，日程设成很久以后，永远不会自己触发。
        internal const string ElevTaskName = "TranslatePopElevated";
        

        public static string ExePath
        {
            get
            {
                try { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
                catch { return ""; }
            }
        }

        private static int _lastExit = -1;

        private static string Run(string args)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args);
                si.CreateNoWindow = true;
                si.UseShellExecute = false;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(si))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(6000);
                    try { _lastExit = p.ExitCode; } catch { _lastExit = -1; }
                    return o;
                }
            }
            catch (Exception ex) { Log.Write("Autostart.Run: " + ex.Message); _lastExit = -1; return ""; }
        }

        /// Task Scheduler 的 XML 定义：只有一个「已禁用」的远期触发器（永远不会自己触发），
        /// 靠 /Run 手动拉起；以当前用户身份、最高权限运行。
        /// 用 XML 而不是 /SC ONCE /SD 是避开 schtasks 的日期格式受系统区域设置影响。
        private const string ElevTaskXml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo><Description>TranslatePop 静默提权（不弹 UAC）</Description></RegistrationInfo>\r\n" +
            "  <Triggers>\r\n" +
            "    <TimeTrigger>\r\n" +
            "      <StartBoundary>2035-12-31T23:59:00</StartBoundary>\r\n" +
            "      <Enabled>false</Enabled>\r\n" +
            "    </TimeTrigger>\r\n" +
            "  </Triggers>\r\n" +
            "  <Principals>\r\n" +
            "    <Principal id=\"Author\">\r\n" +
            "      <UserId>{0}</UserId>\r\n" +
            "      <LogonType>InteractiveToken</LogonType>\r\n" +
            "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
            "    </Principal>\r\n" +
            "  </Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
            "    <Enabled>true</Enabled>\r\n" +
            "    <Hidden>false</Hidden>\r\n" +
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
            "    <WakeToRun>false</WakeToRun>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\"><Exec><Command>{1}</Command></Exec></Actions>\r\n" +
            "</Task>\r\n";

        private static string XmlEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /// 「静默提权」任务是否已就绪：存在，且指向的就是当前这份 exe。
        /// 只判存在不够 —— 换过构建/搬过目录时会指向旧程序。
        public static bool ElevTaskReady()
        {
            try
            {
                string q = Run("/Query /TN " + ElevTaskName + " /FO LIST /V");
                if (_lastExit != 0) return false;
                string want = ExePath;
                if (string.IsNullOrEmpty(want)) return false;
                return q.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        /// 只允许在已提权时调用：创建/修正静默提权任务。
        public static bool EnsureElevTask()
        {
            try
            {
                string exe = ExePath;
                if (string.IsNullOrEmpty(exe)) return false;
                // 已经指向当前这份 exe 就直接返回，避免每次启动都白跑一次 schtasks /Create。
                if (ElevTaskReady()) return true;
                string user = "";
                try { user = System.Security.Principal.WindowsIdentity.GetCurrent().Name; } catch { }
                string xml = string.Format(ElevTaskXml, XmlEscape(user), XmlEscape(exe));
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TranslatePop.ElevTask.xml");
                System.IO.File.WriteAllText(tmp, xml, new System.Text.UnicodeEncoding(false, true));
                string o = Run("/Create /TN " + ElevTaskName + " /XML \"" + tmp + "\" /F");
                bool ok = ElevTaskReady();
                if (!ok)
                {
                    // XML 被拒（老系统 / schema 差异）时退回命令行建法。
                    // 用 /SC MONTHLY /M 12 /D 31 而不是 /SC ONCE /SD：前者不带日期，
                    // 既不受系统区域设置的日期格式影响，也不会因为「时间已过」被拒。
                    // 注意不能 /Change /DISABLE：被禁用的任务 schtasks /Run 会直接拒绝，
                    // 「静默提权」就彻底失效了。这里只在每年 12/31 23:59 多触发一次，
                    // 那时若程序已在运行，新实例会因互斥体直接退出。
                    Log.Write("EnsureElevTask: XML 失败 exit=" + _lastExit + " " + o.Trim() + "，改用 schtasks 命令行");
                    string tr = "\"" + exe + "\"";
                    o = Run("/Create /TN " + ElevTaskName + " /TR " + tr + " /SC MONTHLY /M 12 /D 31 /ST 23:59 /RL HIGHEST /F");
                    ok = ElevTaskReady();
                }
                Log.Write("EnsureElevTask: " + (ok ? "created" : "failed exit=" + _lastExit + " " + o.Trim()));
                try { System.IO.File.Delete(tmp); } catch { }
                return ok;
            }
            catch (Exception ex) { Log.Write("EnsureElevTask: " + ex.Message); return false; }
        }

        /// 删除「静默提权」任务（回到每次启动都弹 UAC 的老行为）。
        public static void RemoveElevTask()
        {
            try
            {
                string o = Run("/Delete /TN " + ElevTaskName + " /F");
                Log.Write("RemoveElevTask: exit=" + _lastExit + " " + o.Trim());
            }
            catch (Exception ex) { Log.Write("RemoveElevTask: " + ex.Message); }
        }

        /// 用「静默提权」任务把自己拉起来 —— 不弹 UAC。成功返回 true。
        public static bool StartViaTask()
        {
            try
            {
                string o = Run("/Run /TN " + ElevTaskName);
                bool ok = _lastExit == 0;
                Log.Write("StartViaTask: " + (ok ? "ok" : "exit=" + _lastExit + " " + o.Trim()));
                return ok;
            }
            catch (Exception ex) { Log.Write("StartViaTask: " + ex.Message); return false; }
        }

        public static bool Enabled
        {
            get { return Run("/Query /TN " + TaskName).IndexOf(TaskName, StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        public static void Set(bool on)
        {
            if (on)
            {
                string tr = "\"" + ExePath + "\"";
                string args = "/Create /TN " + TaskName + " /TR " + tr + " /SC ONLOGON /RL HIGHEST /F";
                string o = Run(args);
                Log.Write("Autostart.Create: " + o.Trim());
            }
            else
            {
                string o = Run("/Delete /TN " + TaskName + " /F");
                Log.Write("Autostart.Delete: " + o.Trim());
            }
        }
    }

    // ---------------- 全局键盘钩子（兜底热键） ----------------
    // RegisterHotKey 对部分组合（如 Win+`）即使注册成功也可能收不到 WM_HOTKEY
    //（被外壳或外设驱动抢先消费）。底层钩子在系统分发前就能看到按键，因此更可靠。
    //
    // 关于「吞键」：只有带 Win 的组合才需要吞。
    //   Win+` 不吞的后果 —— Win 抬起时外壳认为这是一次「纯 Win 单击」，弹出开始菜单。
    //   Alt+` 反过来，绝对不能吞 —— 窗口的规则是「Alt 按下、抬起之间若没有任何按键
    //   到达窗口，Alt 抬起即激活菜单栏」。把 ` 吞掉正好造成这个局面，
    //   于是每次都在当前程序里闪一下菜单栏 / 菜单高亮。
    // 所以吞不吞由 Swallow 决定，由调用方按热键是否带 Win 传进来。
    internal sealed class KeyboardHook : IDisposable
    {
        private Native.LowLevelKeyboardProc _proc;   // 必须保持引用，否则被 GC 回收导致崩溃
        private IntPtr _handle = IntPtr.Zero;
        private uint _mods;
        private uint _vk;
        private bool _swallow;
        private bool _down;                          // 目标键是否仍处于按下状态（用于抑制自动重复）

        public event Action Pressed;

        /// 诊断用：底层收到的每个按键（code, 是否按下）
        public event Action<uint, bool> RawKey;

        public bool Installed { get { return _handle != IntPtr.Zero; } }

        public bool Swallow { get { return _swallow; } }

        public bool Install(uint mods, uint vk, bool swallow)
        {
            Dispose();
            _mods = mods;
            _vk = vk;
            _swallow = swallow;
            _down = false;
            _proc = new Native.LowLevelKeyboardProc(Callback);
            IntPtr hmod = Native.GetModuleHandle(null);
            _handle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, hmod, 0);
            if (_handle == IntPtr.Zero)
                Log.Write("SetWindowsHookEx failed err=" + Marshal.GetLastWin32Error());
            return _handle != IntPtr.Zero;
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                Action<uint, bool> raw = RawKey;
                if (raw != null)
                {
                    try { raw(k.vkCode, down); } catch { }
                }
                if (k.vkCode == _vk)
                {
                    if (!down)
                    {
                        _down = false;              // 松开后允许下一次触发
                        // 带 win 的组合要连抬起一起吞，否则外壳会把它当成一次单击 Win
                        if (_swallow) return (IntPtr)1;
                    }
                    else
                    {
                        // 兜底：若上一条 keyup 被别的钩子吃掉没送到，_down 会一直卡住，
                        // 用物理按键状态自愈，避免热键「按一次就永久失效」。
                        if (_down && !Native.KeyDown((int)_vk)) _down = false;

                        if (!_down && ModsMatch())
                        {
                            _down = true;           // 标记已按下：按住不放的自动重复不再触发
                            Action h = Pressed;
                            if (h != null)
                            {
                                try { h(); } catch (Exception ex) { Log.Write("hook fire: " + ex.Message); }
                            }
                            // 只在带 Win 的组合上吞键。Alt/Ctrl 组合必须放行：
                            // 吞掉后「Alt 按下期间没有任何按键到达窗口」，抬起时必弹菜单栏。
                            // 放行时 Alt+` 是系统键（WM_SYSCHAR），不会真的往正文里插入反引号。
                            if (_swallow) return (IntPtr)1;
                        }
                        else if (_swallow)
                        {
                            return (IntPtr)1;       // 按住期间的自动重复也一并吞掉
                        }
                    }
                }
            }
            return Native.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        /// 修饰键必须与配置完全一致（多了少了都不算），避免带 Shift 之类的组合误触发。
        private bool ModsMatch()
        {
            bool win = Native.KeyDown(Native.VK_LWIN) || Native.KeyDown(Native.VK_RWIN);
            bool ctrl = Native.KeyDown(Native.VK_CONTROL) || Native.KeyDown(Native.VK_LCONTROL) || Native.KeyDown(Native.VK_RCONTROL);
            bool alt = Native.KeyDown(Native.VK_MENU) || Native.KeyDown(Native.VK_LMENU) || Native.KeyDown(Native.VK_RMENU);
            bool shift = Native.KeyDown(Native.VK_SHIFT) || Native.KeyDown(Native.VK_LSHIFT) || Native.KeyDown(Native.VK_RSHIFT);
            bool wantWin = (_mods & Native.MOD_WIN) != 0;
            bool wantCtrl = (_mods & Native.MOD_CONTROL) != 0;
            bool wantAlt = (_mods & Native.MOD_ALT) != 0;
            bool wantShift = (_mods & Native.MOD_SHIFT) != 0;
            return win == wantWin && ctrl == wantCtrl && alt == wantAlt && shift == wantShift;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                try { Native.UnhookWindowsHookEx(_handle); } catch { }
                _handle = IntPtr.Zero;
            }
            _proc = null;
        }
    }

    // ---------------- 热键窗口 ----------------
    internal sealed class HotkeyWindow : NativeWindow
    {
        public const int IdMain = 0x5101;
        public const int IdFallback = 0x5102;
        public event Action<int> Pressed;

        public HotkeyWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "TranslatePop.HotkeyWindow";
            cp.Style = 0;
            cp.ExStyle = 0;
            cp.X = 0;
            cp.Y = 0;
            cp.Width = 0;
            cp.Height = 0;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Action<int> h = Pressed;
                if (h != null) h(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }

    // ---------------- 托盘应用 ----------------
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly Cfg _cfg;
        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hk;
        private readonly Control _sync;
        private PopupForm _popup;
        private bool _busy;
        private string _activeHotkey = "";
        private KeyboardHook _hook;   // 主触发：底层键盘钩子（RegisterHotKey 对 Win+` 会被外壳吞掉）

        public TrayApp(Cfg cfg)
        {
            _cfg = cfg;
            _sync = new Control();
            _sync.CreateControl();
            IntPtr forceSync = _sync.Handle;

            _hk = new HotkeyWindow();
            _hk.Pressed += OnHotkey;

            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Visible = true;
            _tray.Text = "TranslatePop 划词翻译";

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("翻译剪贴板内容", null, delegate { TranslateClipboard(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重新注册热键", null, delegate { RegisterHotkey(true); });
            ToolStripMenuItem auto = new ToolStripMenuItem("开机自启");
            auto.Checked = Autostart.Enabled;
            auto.Click += delegate
            {
                bool now = !Autostart.Enabled;
                Autostart.Set(now);
                auto.Checked = Autostart.Enabled;
                Balloon(now ? "已设置为开机自启" : "已取消开机自启");
            };
            menu.Items.Add(auto);
            if (!IsAdmin())
            {
                menu.Items.Add("启用静默提权（下次启动免 UAC）", null, delegate
                {
                    // /Create /RL HIGHEST 必须要管理员权限，普通权限下必然失败，
                    // 所以这里提权跑一次本程序的一次性 CLI，由它去建任务。
                    Program.RestartElevated("--install-task");
                });
            }
            menu.Items.Add("打开配置", null, delegate { OpenFile(Cfg.ConfigPath); });
            menu.Items.Add("打开日志", null, delegate { OpenFile(Cfg.LogPath); });
            if (!IsAdmin())
            {
                menu.Items.Add("以管理员权限重启（仅当目标程序是管理员运行时才需要）", null, delegate { Program.RestartElevated(); });
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApp(); });
            _tray.ContextMenuStrip = menu;

            RegisterHotkey(false);
            Log.Write("started; build=" + BuildTag + "; hotkey=" + _activeHotkey + "; endpoint=" + _cfg.Endpoint + "; admin=" + IsAdmin());
            WarmUp();
        }

        public const string BuildTag = "2026-11-17-2";

        internal static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// 预热翻译引擎：首次请求要加载词典/模型，约 2 秒；后台提前跑一次，之后每次约 200ms。
        private void WarmUp()
        {
            Thread t = new Thread(delegate ()
            {
                try
                {
                    string err;
                    int ms;
                    Api.Translate(_cfg, "warm up", out err, out ms);
                    Log.Write("warmup=" + ms + "ms");
                }
                catch (Exception ex) { Log.Write("WarmUp: " + ex.Message); }
            });
            t.IsBackground = true;
            t.Start();
        }

        private static void OpenFile(string path)
        {
            try { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); }
            catch { }
        }

        private static Icon MakeIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.Clear(Color.Transparent);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(64, 132, 255)))
                        {
                            g.FillEllipse(b, 0, 0, 31, 31);
                        }
                        using (Font f = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush w = new SolidBrush(Color.White))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("译", f, w, new RectangleF(0, 0, 32, 32), sf);
                        }
                    }
                    IntPtr h = bmp.GetHicon();
                    Icon ic = Icon.FromHandle(h);
                    Icon clone = (Icon)ic.Clone();
                    Native.DestroyIcon(h);
                    return clone;
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void RegisterHotkey(bool announce)
        {
            try { Native.UnregisterHotKey(_hk.Handle, HotkeyWindow.IdMain); } catch { }
            try { Native.UnregisterHotKey(_hk.Handle, HotkeyWindow.IdFallback); } catch { }

            uint mods, vk;

            // 含 Alt 的组合不能交给 RegisterHotKey。
            // 系统注册热键后会把那颗键从输入流里吃掉，于是目标窗口的视角变成
            // 「Alt 按下 → 中间没有任何按键 → Alt 抬起」，而 Windows 的规则正是
            // 「Alt 抬起时若中间无按键，就激活菜单栏」——这就是每次按 Alt+` 都先闪一下
            // 菜单/菜单高亮的原因。
            // 所以这类组合改用底层钩子，并且【不吞键】：按键照常下发，窗口收到过别的键，
            // 菜单栏就不会被激活，热键照样在钩子里触发。
            if (Hotkey.TryParse(_cfg.Hotkey, out mods, out vk) && (mods & Native.MOD_ALT) != 0)
            {
                string desc = Hotkey.Describe(mods, vk);
                if (InstallHook(mods, vk))
                {
                    _activeHotkey = desc;
                    _tray.Text = "TranslatePop 划词翻译（" + desc + "）";
                    Log.Write("hotkey via hook only (alt combo): " + desc);
                    if (announce) Balloon("热键已启用：" + desc);
                    return;
                }
                Log.Write("hook-only failed, falling back to RegisterHotKey for " + desc);
            }

            bool ok = false;
            string used = "";
            if (Hotkey.TryParse(_cfg.Hotkey, out mods, out vk))
            {
                ok = Native.RegisterHotKey(_hk.Handle, HotkeyWindow.IdMain, mods | Native.MOD_NOREPEAT, vk);
                if (ok) used = Hotkey.Describe(mods, vk);
            }
            if (!ok && Hotkey.TryParse(_cfg.FallbackHotkey, out mods, out vk))
            {
                ok = Native.RegisterHotKey(_hk.Handle, HotkeyWindow.IdFallback, mods | Native.MOD_NOREPEAT, vk);
                if (ok) used = Hotkey.Describe(mods, vk) + "（备用）";
            }

            if (ok)
            {
                _activeHotkey = used;
                _tray.Text = "TranslatePop 划词翻译（" + used + "）";
                Log.Write("hotkey registered: " + used);
                InstallHook(mods, vk);
                if (announce) Balloon("热键已启用：" + used);
            }
            else
            {
                _activeHotkey = "";
                _tray.Text = "TranslatePop（热键注册失败）";
                Balloon("热键注册失败：请修改 config.ini 里的 hotkey 后重启");
                Log.Write("RegisterHotKey failed for " + _cfg.Hotkey);
            }
        }

        /// 底层键盘钩子：RegisterHotKey 对 Win+按键 这类组合会返回成功但收不到 WM_HOTKEY
        /// （按键在下发前被外壳或外设驱动消费），钩子在更早的环节就能拿到。
        /// 是否吞键见 Swallow：只有 Win 组合需要吞 —— Alt/Ctrl 组合若吞键，
        /// 目标窗口在「Alt 按下→抬起」之间收不到任何按键，Windows 就会激活菜单栏。
        private bool InstallHook(uint mods, uint vk)
        {
            if (_hook == null) _hook = new KeyboardHook();
            _hook.Pressed -= OnHookPressed;
            _hook.Pressed += OnHookPressed;
            bool swallow = (mods & Native.MOD_WIN) != 0;
            bool ok = _hook.Install(mods, vk, swallow);
            Log.Write("keyboard hook installed=" + ok + " swallow=" + swallow);
            return ok;
        }

        private void OnHookPressed()
        {
            Log.Write("hotkey hit (hook) busy=" + _busy);
            OnHotkey(0);
        }

        private void Balloon(string msg)
        {
            try
            {
                _tray.BalloonTipTitle = "TranslatePop";
                _tray.BalloonTipText = msg;
                _tray.ShowBalloonTip(2500);
            }
            catch { }
        }

        private void OnHotkey(int id)
        {
            Log.Write("hotkey hit busy=" + _busy);
            if (_busy) return;
            _busy = true;
            Thread t = new Thread(delegate ()
            {
                try { DoSelectionTranslate(); }
                catch (Exception ex) { Log.Write("DoSelectionTranslate: " + ex); }
                finally { _busy = false; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        private System.Diagnostics.Stopwatch _stage;

        private void DoSelectionTranslate()
        {
            _stage = System.Diagnostics.Stopwatch.StartNew();
            Log.Write("t0 hotkey->work");
            ReanchorCard();
            Grabber.WaitModifiersReleased(700);
            Log.Write("t1 mods released @" + _stage.ElapsedMilliseconds);
            Thread.Sleep(20);

            bool restored;
            int grabMs;
            string selected = Grabber.ReadSelection(out restored, out grabMs);
            Log.Write("grab=" + grabMs + "ms restored=" + restored + " len=" + selected.Length + " @" + _stage.ElapsedMilliseconds);

            if (selected.Length == 0)
            {
                ShowCard("没有取到选中文本\n先选中文字，再按 " + _activeHotkey, null, true, true);
                return;
            }
            if (selected.Length > _cfg.MaxChars) selected = selected.Substring(0, _cfg.MaxChars);
            Log.Write("t2 grabbed @" + _stage.ElapsedMilliseconds);
            // 新一轮翻译：卡片挪到当前鼠标旁，不留在上一处
            ShowCard("翻译中…", null, false, true);

            string err;
            int ms;
            string sub;
            string result = TranslateSmart(_cfg, selected, out sub, out err, out ms);
            Log.Write("translate=" + ms + "ms err=" + (err == null ? "-" : err) + (sub == null ? "" : " code"));

            Log.Write("t3 translated @" + _stage.ElapsedMilliseconds);
            if (result == null) ShowCard("翻译失败：" + err, null, true, false);
            else ShowCard(result, sub, false, false);
            Log.Write("t4 card shown @" + _stage.ElapsedMilliseconds);
        }

        private void TranslateClipboard()
        {
            Thread t = new Thread(delegate ()
            {
                try
                {
                    string text = "";
                    try
                    {
                        if (Clipboard.ContainsText()) text = Clipboard.GetText().Trim();
                    }
                    catch { }
                    if (text.Length == 0) { ShowCard("剪贴板是空的", true); return; }
                    if (text.Length > _cfg.MaxChars) text = text.Substring(0, _cfg.MaxChars);
                    string err;
                    int ms;
                    string sub;
                    string result = TranslateSmart(_cfg, text, out sub, out err, out ms);
                    if (result == null) ShowCard("翻译失败：" + err, true);
                    else ShowCard(result, sub, false);
                }
                catch (Exception ex) { Log.Write("TranslateClipboard: " + ex); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        /// 代码模式：整句走一次翻译，标识符的每个词用词表/批量接口补上，拼成第二行对照。
        /// 返回整句译文；sub 为 null 表示只有一行。
        internal static string TranslateSmart(Cfg cfg, string text, out string sub, out string err, out int ms)
        {
            sub = null;
            bool wantCode = cfg.CodeMode && (CodeText.LooksLikeCode(text) || CodeText.HasIdentifiers(text));
            string[] words = wantCode ? CodeText.Split(text) : null;
            if (words == null || words.Length == 0)
                return Api.Translate(cfg, text, out err, out ms);

            // 词表命中的词不再花接口额度，只把没收录的送去批量翻译
            List<string> unknown = new List<string>();
            Dictionary<string, int> slot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string w in words)
            {
                if (w.Length == 0) continue;
                string z;
                if (CodeText.TryGloss(w, out z)) continue;
                if (slot.ContainsKey(w)) continue;
                slot[w] = unknown.Count;
                unknown.Add(w);
            }

            // 句子里夹着的驼峰标识符先摊开（UserServiceImpl -> User Service Impl），
            // 否则 Mtranserver 会把它们原样留在译文里。
            List<string> idents;
            string desugared = CodeText.Desugar(text, out idents);

            // 一次往返拿三样：整句直译、摊开标识符后的整句、未收录单词
            string[] texts = new string[2 + unknown.Count];
            texts[0] = text;
            texts[1] = desugared;
            for (int i = 0; i < unknown.Count; i++) texts[i + 2] = unknown[i];

            string berr;
            int bms;
            string[] res = Api.TranslateBatch(cfg, texts, out berr, out bms);
            if (res == null || res.Length == 0)
            {
                // 批量失败就退回单句，至少让用户看到整句译文
                Log.Write("batch fallback: " + (berr == null ? "-" : berr));
                return Api.Translate(cfg, text, out err, out ms);
            }
            err = berr;
            ms = bms;

            string[] zh = new string[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                string z;
                if (CodeText.TryGloss(words[i], out z)) { zh[i] = z; continue; }
                int k;
                if (slot.TryGetValue(words[i], out k) && 2 + k < res.Length) zh[i] = res[2 + k];
            }
            // 只有真的像代码（或夹着标识符）才给第二行逐词对照，普通句子保持单行
            if (CodeText.LooksLikeCode(text) || idents.Count > 0)
            {
                string line = CodeText.BuildSub(words, zh);
                if (line.Length > 0) sub = line;
            }

            bool singleIdent = CodeText.IsSingleIdentifier(text);
            // 有标识符的多词行（public class UserServiceImpl ...）也按代码处理：
            // 逐词替换能保住括号/等号/标点结构，比 API 整句更贴原文。
            bool isCode = CodeText.LooksLikeCode(text) || idents.Count > 0;  // 有标识符就按代码处理

            // 单个标识符且每个词都在词表里：直接拼中文，最稳（getUserById -> 获取用户按ID）
            if (singleIdent)
            {
                string one;
                if (CodeText.GlossJoin(text, out one)) return one;
            }

            // 候选 A：原文直译，把残留的标识符整体换掉
            string a = res[0] == null ? null : CodeText.TidySpaces(res[0].Trim());
            if (a != null && idents.Count > 0) a = CodeText.PatchIdentifiers(a, idents);

            // 候选 B：摊开标识符后的整句直译，再把残留的英文单词换成中文
            string b = (res.Length > 1) ? res[1] : null;
            if (b != null) b = CodeText.PatchLeftovers(CodeText.TidySpaces(b.Trim()));

            // 候选 C：按原文骨架逐词替换（保留括号、等号、标点结构）
            int rep;
            string c = CodeText.MixLine(text, out rep);
            if (rep < 1 || !CodeText.HasCjk(c)) c = null;

            // 挑法：代码行优先保住结构（C > B > A），普通句子优先语序（A > B > C）。
            // 只接受真的译出中文、且英文残留不高于直译的候选。
            string main;
            if (isCode) { main = Pick(text, c, b, a); }
            else { main = Pick(text, a, b, c); }
            if (main == null || !CodeText.HasCjk(main)) main = sub;
            return main;
        }

        /// 挑候选：英文残留越少越好；原文里的代码标点（括号/点/等号/分号）保留得越多也加分。
        private static string Pick(string src, params string[] cands)
        {
            int srcPunct = CountPunct(src);
            string best = null;
            float bs = 2.0f;
            foreach (string c in cands)
            {
                if (c == null || c.Length == 0) continue;
                if (!CodeText.HasCjk(c)) continue;
                float s = CodeText.LatinRatio(c);
                // 结构加分：最多抵 0.10 的英文残留，保证带括号的语句不会被意译版挤掉
                if (srcPunct > 0) s -= 0.10f * Math.Min(1f, (float)CountPunct(c) / (float)srcPunct);
                if (s < bs) { bs = s; best = c; }
            }
            return best;
        }

        /// 数代码标点（不数句号/逗号，那是自然语言里的）
        private static int CountPunct(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(' || c == ')' || c == '{' || c == '}' || c == '[' || c == ']'
                    || c == '=' || c == ';' || c == '<' || c == '>' || c == '_'
                    || c == '.' || c == ',' || c == ':') n++;
            }
            return n;
        }

        /// 两个候选里挑英文残留更少的那个；差距不明显就保留前者（更贴近原文语序）。
        private static string Better(string cur, string cand)
        {
            if (cur == null || cur.Length == 0) return cand;
            if (cand == null || cand.Length == 0) return cur;
            if (!CodeText.HasCjk(cand)) return cur;
            if (!CodeText.HasCjk(cur)) return cand;
            float s1 = CodeText.LatinRatio(cur);
            float s2 = CodeText.LatinRatio(cand);
            return (s2 < s1 - 0.05f) ? cand : cur;
        }

        /// 已有卡片就原地换文字，没有才新建（翻译中 → 译文，不闪烁）。
        private void ShowCard(string text, bool isError)
        {
            ShowCard(text, null, isError);
        }

        private void ShowCard(string text, string sub, bool isError)
        {
            ShowCard(text, sub, isError, false);
        }

        /// 立即把当前卡片挪到鼠标旁（不等取词、不等译文，按下热键就跳过去）。
        private void ReanchorCard()
        {
            if (!_sync.IsHandleCreated) return;
            try
            {
                _sync.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        if (_popup != null && !_popup.IsDisposed) _popup.AnchorToCursor();
                    }
                    catch (Exception ex) { Log.Write("ReanchorCard: " + ex.Message); }
                }));
            }
            catch (Exception ex) { Log.Write("ReanchorCard.invoke: " + ex.Message); }
        }

        /// reanchor=true 时把卡片挪到当前鼠标位置（每次新翻译开头调用）。
        private void ShowCard(string text, string sub, bool isError, bool reanchor)
        {
            if (!_sync.IsHandleCreated) return;
            try
            {
                _sync.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        if (_popup != null && !_popup.IsDisposed)
                        {
                            _popup.SetText(text, sub, isError);
                            if (reanchor) _popup.AnchorToCursor();
                            return;
                        }
                        PopupForm f = new PopupForm(text, isError, _cfg, null);
                        f.SetText(text, sub, isError);
                        _popup = f;
                        f.Show();
                    }
                    catch (Exception ex) { Log.Write("ShowCard: " + ex.Message); }
                }));
            }
            catch (Exception ex) { Log.Write("ShowCard.invoke: " + ex.Message); }
        }

        private void ExitApp()
        {
            try { if (_hook != null) _hook.Dispose(); } catch { }
            try { Native.UnregisterHotKey(_hk.Handle, HotkeyWindow.IdMain); } catch { }
            try { Native.UnregisterHotKey(_hk.Handle, HotkeyWindow.IdFallback); } catch { }
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            try { _hk.DestroyHandle(); } catch { }
            ExitThread();
        }
    }

    // ---------------- CLI / 自检 ----------------
    internal static class Cli
    {
        private static string _outFile;
        private static readonly StringBuilder Buf = new StringBuilder();

        private static void Emit(string s)
        {
            Buf.AppendLine(s);
            try { Console.WriteLine(s); }
            catch { }
            Flush();
        }

        private static void Flush()
        {
            if (_outFile == null) return;
            try { File.WriteAllText(_outFile, Buf.ToString(), new UTF8Encoding(false)); }
            catch { }
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        public static int Run(string[] args)
        {
            Native.AttachConsole(-1);
            _outFile = Arg(args, "--out");
            string mode = args[0].TrimStart('-').ToLowerInvariant();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 由托盘菜单提权调用：以管理员身份建「静默提权」计划任务。
            // 之后每次启动都能直接 /Run 该任务静默拿到管理员权限，不再弹 UAC。
            if (mode == "install-task" || mode == "uninstall-task")
            {
                bool on = mode == "install-task";
                bool ready;
                if (on) ready = Autostart.EnsureElevTask();
                else { Autostart.RemoveElevTask(); ready = !Autostart.ElevTaskReady(); }
                Log.Write((on ? "install-task" : "uninstall-task") + "=" + (ready ? "OK" : "FAIL")
                    + "; admin=" + TrayApp.IsAdmin() + "; exe=" + Autostart.ExePath);
                Emit((on ? "install-task" : "uninstall-task") + "=" + (ready ? "OK" : "FAIL"));
                Flush();
                if (ready && on)
                {
                    // 任务装好后立刻用它把程序拉起来，验证「静默提权」这条链路真的通；
                    // 拉起来的是真正的常驻实例，本次这个临时进程随即退出。
                    System.Threading.Thread.Sleep(600);
                    if (!Autostart.StartViaTask())
                        Log.Write("install-task: 任务已建好，但 /Run 失败，下次双击会走 runas");
                }
                return ready ? 0 : 1;
            }

            Cfg cfg = Cfg.Load();
            Emit("config=" + Cfg.ConfigPath);

            if (mode == "check")
            {
                Emit("endpoint=" + cfg.Endpoint);
                Emit("from=" + cfg.From + " to=" + cfg.To);
                Emit("hotkey=" + cfg.Hotkey + " fallback=" + cfg.FallbackHotkey);
                Emit("health=" + Api.Clip(TryGet(cfg, "/health"), 120));
                Emit("version=" + Api.Clip(TryGet(cfg, "/version"), 120));
                string err;
                int ms;
                string r1 = Api.Translate(cfg, "serendipity", out err, out ms);
                Emit("word=serendipity -> " + (r1 == null ? ("ERROR " + err) : r1) + "  (" + ms + "ms)");
                string r2 = Api.Translate(cfg, "The quick brown fox jumps over the lazy dog.", out err, out ms);
                Emit("sentence -> " + (r2 == null ? ("ERROR " + err) : r2) + "  (" + ms + "ms)");
                Emit("RESULT=" + ((r1 != null && r2 != null) ? "OK" : "FAIL"));
                return r1 != null && r2 != null ? 0 : 1;
            }

            if (mode == "text")
            {
                string text = Arg(args, "--text");
                if (string.IsNullOrEmpty(text) && args.Length > 1) text = args[1];
                if (string.IsNullOrEmpty(text)) { Emit("usage: TranslatePop.exe --text \"hello\""); return 2; }
                string err;
                int ms;
                string r = Api.Translate(cfg, text, out err, out ms);
                Emit("in=" + text);
                Emit("out=" + (r == null ? ("ERROR " + err) : r));
                Emit("ms=" + ms);
                PopupForm f = new PopupForm(r == null ? ("翻译失败：" + err) : r, r == null, cfg, null);
                Application.Run(f);
                return r == null ? 1 : 0;
            }

            if (mode == "code")
            {
                string one = Arg(args, "--text");
                string[] samples = string.IsNullOrEmpty(one)
                    ? new string[] { "getUserById", "userService.getUserById(id)", "SNAKE_CASE_CONST", "private String getUserById(int id) {", "exception", "The quick brown fox jumps over the lazy dog." }
                    : new string[] { one };
                bool allOk = true;
                foreach (string s in samples)
                {
                    string sub, err;
                    int ms;
                    string r = TrayApp.TranslateSmart(cfg, s, out sub, out err, out ms);
                    Emit("in  = " + s);
                    Emit("out = " + (r == null ? ("ERROR " + err) : r));
                    Emit("sub = " + (sub == null ? "(无)" : sub));
                    Emit("ms  = " + ms);
                    Emit("");
                    if (r == null) allOk = false;
                }
                Emit("RESULT=" + (allOk ? "OK" : "FAIL"));
                return allOk ? 0 : 1;
            }

            if (mode == "mix")
            {
                string one = Arg(args, "--text");
                if (string.IsNullOrEmpty(one)) one = "private String getUserById(int id) {";
                string[] ps = CodeText.Split(one);
                Emit("split = " + string.Join(" | ", ps));
                foreach (string p in ps)
                {
                    string zz;
                    Emit("  " + p + " -> " + (CodeText.TryGloss(p, out zz) ? zz : "(未收录)"));
                }
                int rep;
                Emit("mix   = " + CodeText.MixLine(one, out rep) + "  (replaced=" + rep + ")");
                return 0;
            }

            if (mode == "demo")
            {
                string text = Arg(args, "--text");
                if (string.IsNullOrEmpty(text)) text = "意外收获、机缘巧合；发现珍奇事物的本领\n-- serendipity\n这是一行用来测试自动换行的较长中文译文，看看卡片的宽度与行距是否合适。";
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Point at = new Point(wa.Left + wa.Width / 2 - 230, wa.Top + wa.Height / 2 - 60);
                PopupForm f = new PopupForm(text, false, cfg, at);
                f.Show();
                DateTime end = DateTime.Now.AddMilliseconds(1500);
                while (DateTime.Now < end)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                Emit("demo visible=" + f.Visible + " bounds=" + f.Bounds.X + "," + f.Bounds.Y + " " + f.Width + "x" + f.Height);
                Application.Run(f);
                return 0;
            }

            if (mode == "render")
            {
                string png = Arg(args, "--png");
                string text = Arg(args, "--text");
                string sub = Arg(args, "--sub");
                bool isErr = Arg(args, "--error") != null;
                if (string.IsNullOrEmpty(text)) text = "意外收获、机缘巧合；发现珍奇事物的本领\n-- serendipity";
                PopupForm f = new PopupForm(text, isErr, cfg, new Point(100, 100));
                if (!string.IsNullOrEmpty(sub)) f.SetText(text, sub, isErr);
                f.Show();
                Application.DoEvents();
                Thread.Sleep(120);
                Application.DoEvents();
                Emit("size=" + f.Width + "x" + f.Height + (string.IsNullOrEmpty(sub) ? "" : " (two-line)"));
                if (png != null)
                {
                    try
                    {
                        using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                        {
                            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                            bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        Emit("png=" + png);
                    }
                    catch (Exception ex) { Emit("png error: " + ex.Message); }
                }
                f.Close();
                return 0;
            }

            if (mode == "grab")
            {
                Emit("先选中文本再运行 --grab");
                bool restored;
                int ms;
                string s = Grabber.ReadSelection(out restored, out ms);
                Emit("grabMs=" + ms + " restored=" + restored + " len=" + s.Length);
                Emit("text=" + Api.Clip(s, 200));
                return s.Length > 0 ? 0 : 1;
            }

            if (mode == "hooktest")
            {
                uint mods, vk;
                bool parsed = Hotkey.TryParse(cfg.Hotkey, out mods, out vk);
                Emit("target=" + Hotkey.Describe(mods, vk) + " parsed=" + parsed);
                int seconds = 8;
                string sv = Arg(args, "--seconds");
                if (sv != null) int.TryParse(sv, out seconds);

                KeyboardHook hook = new KeyboardHook();
                hook.RawKey += delegate (uint code, bool down)
                {
                    if (down) Emit("key 0x" + code.ToString("X2") + (code == vk ? "  <== 目标键" : ""));
                };
                hook.Pressed += delegate { Emit("MATCH fired"); };
                bool ok = hook.Install(mods, vk, (mods & Native.MOD_WIN) != 0);
                Emit("installed=" + ok);
                if (!ok) { hook.Dispose(); return 1; }

                bool selfTest = false;
                for (int ai = 0; ai < args.Length; ai++)
                    if (string.Equals(args[ai], "--self", StringComparison.OrdinalIgnoreCase)) selfTest = true;

                if (selfTest)
                {
                    // 必须让主线程先进入消息循环，否则钩子回调会被丢弃（LL 钩子超时）
                    System.Threading.Thread inj = new System.Threading.Thread(delegate ()
                    {
                        System.Threading.Thread.Sleep(800);
                        Emit("self-injecting target keys...");
                        Native.keybd_event((byte)Native.VK_LWIN, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(30);
                        Native.keybd_event(0xC0, 0, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(30);
                        Native.keybd_event(0xC0, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                        Native.keybd_event((byte)Native.VK_LWIN, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    });
                    inj.IsBackground = true;
                    inj.Start();
                }

                DateTime until = DateTime.Now.AddSeconds(seconds);
                while (DateTime.Now < until)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(15);
                }
                hook.Dispose();
                Emit("done");
                return 0;
            }
            if (mode == "hotkeytest")
            {
                uint mods, vk;
                bool parsed = Hotkey.TryParse(cfg.Hotkey, out mods, out vk);
                Emit("parsed=" + parsed + " mods=" + mods + " vk=0x" + vk.ToString("X2") + " desc=" + Hotkey.Describe(mods, vk));
                HotkeyWindow w = new HotkeyWindow();
                bool ok = Native.RegisterHotKey(w.Handle, 0x5201, mods | Native.MOD_NOREPEAT, vk);
                Emit("register=" + ok + " lastErr=" + Marshal.GetLastWin32Error());
                if (ok)
                {
                    Native.UnregisterHotKey(w.Handle, 0x5201);
                    Emit("unregistered ok");
                }
                w.DestroyHandle();
                return ok ? 0 : 1;
            }

            Emit("unknown mode: " + mode);
            Emit("modes: check | text | demo | render | grab | hotkeytest");
            return 2;
        }

        private static string TryGet(Cfg cfg, string path)
        {
            try { return Api.Get(cfg, path); }
            catch (Exception ex) { return "ERROR: " + ex.Message; }
        }
    }

    internal static class Program
    {
        private const string MutexName = "TranslatePop_SingleInstance_v1";
        private static Mutex _mutex;

        /// 托盘菜单用：先放掉单实例互斥体再以管理员身份重启，
        /// 否则提权后的新实例会被自己拦住。只有目标程序本身以管理员运行时才需要。
        internal static void RestartElevated()
        {
            RestartElevated(null);
        }

        internal static void RestartElevated(string extraArgs)
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            try
            {
                if (_mutex != null) { try { _mutex.ReleaseMutex(); } catch { } try { _mutex.Dispose(); } catch { } _mutex = null; }
                // /Run 计划任务和直接 runas 都不能立刻生效（任务有启动延迟），
                // 先延时把旧实例放掉，避免新实例起来时被单实例互斥体挡住。
                System.Diagnostics.ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo();
                si.FileName = exe;
                if (!string.IsNullOrEmpty(extraArgs)) si.Arguments = extraArgs;
                si.UseShellExecute = true;
                si.Verb = "runas";
                si.WorkingDirectory = System.IO.Path.GetDirectoryName(exe);
                System.Diagnostics.Process.Start(si);
            }
            catch (Exception ex)
            {
                Log.Write("RestartElevated: " + ex.Message);
                System.Windows.Forms.MessageBox.Show("提权失败（可能取消了 UAC）：" + ex.Message, "TranslatePop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Environment.Exit(0);
        }

        /// 调试用：exe 名带 Dev 或设了 TRANSLATEPOP_NOELEVATE=1 时不提权，
        /// 这样能以普通权限启动来复现/对比 UIPI 现象。
        private static bool SkipElevate()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("TRANSLATEPOP_NOELEVATE") == "1") return true;
                string n = System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return n != null && n.IndexOf("Dev", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        /// 以管理员身份重启自己（UAC 会弹一次）。成功返回 true。
        private static bool Elevate()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                System.Diagnostics.ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo();
                si.FileName = exe;
                si.UseShellExecute = true;
                si.Verb = "runas";
                si.WorkingDirectory = System.IO.Path.GetDirectoryName(exe);
                System.Diagnostics.Process.Start(si);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Elevate: " + ex.Message);
                return false;
            }
        }

        /// 已有实例在跑时静默退出，不弹 UAC、不弹提示框。
        /// 放在提权之前判断：低权限进程也能 OpenExisting 由高权限实例创建的命名互斥体，
        /// 所以能避免「双击 → 弹 UAC → 提权成功后又发现自己多余」这种多余的一次提权。
        private static bool AlreadyRunning()
        {
            // 被计划任务/提权拉起时可能和「刚点了双击、正在退出的那个进程」撞在一起：
            // 它还没放掉互斥体，我们就会误判为「已在运行」而静默退出，结果一个实例都没留下。
            // 所以提权实例这边多等一会儿。
            int waitMs = TrayApp.IsAdmin() ? 3000 : 0;
            int waited = 0;
            while (true)
            {
                try
                {
                    using (Mutex m = Mutex.OpenExisting(MutexName)) { }
                    if (waited >= waitMs) return true;
                }
                catch (WaitHandleCannotBeOpenedException) { return false; }
                catch (UnauthorizedAccessException) { return true; }   // 存在但无权访问 = 已有实例
                catch (Exception ex) { Log.Write("AlreadyRunning: " + ex.Message); return false; }
                System.Threading.Thread.Sleep(250);
                waited += 250;
            }
        }

        /// 启动时取得管理员权限的总策略，详见 Cfg.ElevateMode。
        /// 返回 true 表示已经通过某种方式启动/重启了本程序，调用方应立刻退出。
        private static bool TryElevate(Cfg cfg)
        {
            string mode = cfg != null ? cfg.ElevateMode : "auto";
            if (mode == "off") { Log.Write("elevate=off: 以普通权限启动"); return false; }

            if (mode != "on" && Autostart.ElevTaskReady())
            {
                if (Autostart.StartViaTask())
                {
                    // schtasks /Run 只要把请求交出去就返回 0，任务本身可能启动失败
                    // （被策略拦、exe 被替换、账户不匹配…）。这里等一会儿看有没有
                    // 新的实例把互斥体建起来：等到了说明真的起来了；等不到就退回 runas，
                    // 免得「双击之后什么都没有发生」。
                    if (WaitForInstance(4000))
                    {
                        Log.Write("elevate=auto: 已通过计划任务静默重启（不弹 UAC）");
                        return true;
                    }
                    Log.Write("elevate=auto: 计划任务已下发但没有实例起来，退回 runas");
                }
                else Log.Write("elevate=auto: 计划任务启动失败，退回 runas");
            }

            if (Elevate()) return true;
            return false;
        }

        /// 等一个别的实例把互斥体建起来（用于确认计划任务真的把程序拉起来了）。
        private static bool WaitForInstance(int ms)
        {
            int waited = 0;
            while (waited < ms)
            {
                System.Threading.Thread.Sleep(250);
                waited += 250;
                try
                {
                    using (Mutex m = Mutex.OpenExisting(MutexName)) { }
                    return true;
                }
                catch (WaitHandleCannotBeOpenedException) { }
                catch (UnauthorizedAccessException) { return true; }
                catch { }
            }
            return false;
        }

        /// 首次以管理员身份运行时，把 exe 自身和它所在目录的完整性级别抬到 Medium。
        /// exe 如果落在 Low IL 目录（例如被沙箱/低权限程序创建的工作区），
        /// 从 Low IL 上下文走 runas 提权会多弹一个
        /// 「打开文件 - 安全警告 / 无法验证发布者」；改成 Medium IL 后只剩标准 UAC。
        /// 目录也要改：(OI)(CI) 默认会继续把 Low IL 套给以后 build.cmd 生成的新 exe。
        /// 改 IL 需要权限，所以只在已提权时做；失败无副作用。
        private static void HealIntegrityLabel()
        {
            string exe = null;
            try { exe = System.Reflection.Assembly.GetExecutingAssembly().Location; }
            catch { }
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
                SetIntegrity(exe, "Medium");
            try
            {
                string dir = System.IO.Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    SetIntegrity(dir, "(OI)(CI)Medium");
            }
            catch (Exception ex) { Log.Write("heal IL dir failed: " + ex.Message); }
        }

        private static void SetIntegrity(string path, string level)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo("icacls.exe", "\"" + path + "\" /setintegritylevel " + level);
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(si))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);
                    Log.Write("heal IL " + level + " -> " + System.IO.Path.GetFileName(path) + ": exit=" + p.ExitCode
                        + (p.ExitCode == 0 ? "" : " out=" + o.Trim() + " err=" + e.Trim()));
                }
            }
            catch (Exception ex) { Log.Write("heal IL failed: " + ex.Message); }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                try { return Cli.Run(args); }
                catch (Exception ex)
                {
                    try { Console.WriteLine("fatal: " + ex); }
                    catch { }
                    Log.Write("CLI fatal: " + ex);
                    return 9;
                }
            }

            // 已经有实例（不论它是提权还是普通权限）就直接退出：
            // 这样重复双击不会先弹一次 UAC 再弹「已经在运行了」。
            if (AlreadyRunning()) return 0;

            if (!TrayApp.IsAdmin() && !SkipElevate())
            {
                Cfg preCfg = Cfg.Load();
                if (TryElevate(preCfg)) return 0;
                if (preCfg.ElevateMode != "off")
                {
                    MessageBox.Show("提权被取消，程序将以普通权限启动。\n\n此时无法向「以管理员身份运行」的程序（如 IDEA）取词。", "TranslatePop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("ThreadException: " + e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Write("Unhandled: " + e.ExceptionObject); };

            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);
            _mutex = mutex;
            if (!created)
            {
                MessageBox.Show("TranslatePop 已经在运行了（看右下角托盘）。", "TranslatePop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            try
            {
                Cfg cfg = Cfg.Load();
                if (TrayApp.IsAdmin())
                {
                    HealIntegrityLabel();
                    // 第一次以管理员身份跑起来时顺手把计划任务建好，
                    // 之后双击就走「任务静默启动」，不再弹 UAC。
                    if (cfg.ElevateMode == "auto") Autostart.EnsureElevTask();
                }
                Application.Run(new TrayApp(cfg));
            }
            catch (Exception ex)
            {
                Log.Write("Main fatal: " + ex);
                MessageBox.Show("启动失败：" + ex.Message + "\n\n日志：" + Cfg.LogPath, "TranslatePop", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 9;
            }
            finally
            {
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
