using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// ====================================================================== BULLETPROOF ACTIVATION SYSTEM (WIN 7 / 10 / 11)
static class LicenseManager
{
    public const string PortalUrl = "https://lite-media-viewer.vercel.app";
    
    // Yeh secret key sirf aapki app ko pata hai. Iske baghair koi fake registry key nahi bana sakta!
    private const string SecretSalt = "LMV_SuperSecretKey_99887766!";

    public static string GetHwid()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                if (key != null)
                {
                    object val = key.GetValue("MachineGuid");
                    if (val != null) return val.ToString();
                }
            }
        }
        catch { }
        return Environment.MachineName;
    }

    // Hash generator function
    private static string GenerateSignature(string token, string hwid)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(token + hwid + SecretSalt);
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }

    public static bool IsActivated()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\LiteMediaViewer"))
            {
                if (key != null)
                {
                    string savedToken = key.GetValue("Token") as string;
                    string savedSig = key.GetValue("Signature") as string;
                    
                    if (!string.IsNullOrEmpty(savedToken) && !string.IsNullOrEmpty(savedSig))
                    {
                        // Current PC ka hash bana kar saved hash se match karein
                        string currentHwid = GetHwid();
                        string expectedSig = GenerateSignature(savedToken, currentHwid);
                        
                        // Agar kisi ne registry mein khud kuch likha hoga to hash match nahi karega!
                        return savedSig == expectedSig;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public static bool SaveActivation(string token)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\LiteMediaViewer"))
            {
                if (key == null) return false;
                string hwid = GetHwid();
                key.SetValue("Token", token);
                key.SetValue("Signature", GenerateSignature(token, hwid));
                return true;
            }
        }
        catch { return false; }
    }

    public static bool VerifyOnlineViaBrowser(string token, out string message)
    {
        message = "Unknown error";
        token = token.Trim().ToUpper();
        string hwid = GetHwid();
        string cleanPortalUrl = PortalUrl.TrimEnd('/');
        int port;
        TcpListener listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;

            string url = string.Format("{0}/api/activate?token={1}&hwid={2}&port={3}",
                cleanPortalUrl, Uri.EscapeDataString(token), Uri.EscapeDataString(hwid), port);
            
            Process.Start(url);

            DateTime startWait = DateTime.Now;
            while ((DateTime.Now - startWait).TotalSeconds < 20)
            {
                if (listener.Pending())
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string requestLine = reader.ReadLine() ?? "";
                        
                        bool isSuccess = requestLine.Contains("status=success");
                        bool isError = requestLine.Contains("status=error");
                        bool saveFailed = false;
                        if (isSuccess && !SaveActivation(token))
                        {
                            isSuccess = false;
                            saveFailed = true;
                            message = "The token was verified, but activation could not be saved to this PC.";
                        }

                        string htmlResponse = isSuccess 
                            ? "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n<html><body style='background:#1a1b1e;color:#4caf50;text-align:center;font-family:sans-serif;padding-top:100px;'><h2>Activation Successful!</h2><p style='color:#ccc;'>You can close this tab and return to Lite Media Viewer.</p><script>setTimeout(function(){window.close();}, 3000);</script></body></html>"
                            : "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n<html><body style='background:#1a1b1e;color:#ff5252;text-align:center;font-family:sans-serif;padding-top:100px;'><h2>Activation Failed</h2><p style='color:#ccc;'>Verification could not be completed. Return to app for details.</p></body></html>";

                        byte[] responseBytes = Encoding.UTF8.GetBytes(htmlResponse);
                        stream.Write(responseBytes, 0, responseBytes.Length);
                        stream.Flush();
                        listener.Stop();

                        if (isSuccess)
                        {
                            message = "Activated successfully!";
                            return true;
                        }
                        if (saveFailed) return false;
                        
                        if (isError)
                        {
                            if (requestLine.Contains("Token_Already_Used")) message = "This token has already been used and is expired.";
                            else message = "Invalid token entered.";
                            return false;
                        }
                    }
                }
                Application.DoEvents();
                Thread.Sleep(50);
            }

            listener.Stop();
            message = "Browser verification timed out. Did you close the browser tab?";
            return false;
        }
        catch (Exception ex)
        {
            if (listener != null) try { listener.Stop(); } catch { }
            message = "Bridge Error: " + ex.Message;
            return false;
        }
    }
}
// ====================================================================== ACTIVATION DIALOG
class ActivationDialog : Form
{
    TextBox tbToken;
    Button btnActivate, btnGetKey;
    Label lblStatus;

    public ActivationDialog()
    {
        Text = "Lite Media Viewer - Activation";
        ClientSize = new Size(460, 230);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = Color.FromArgb(26, 27, 30);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9.5f);

        Label lbl = new Label();
        lbl.Text = "Enter your free lifetime activation token below:";
        lbl.SetBounds(20, 20, 420, 24);

        tbToken = new TextBox();
        tbToken.SetBounds(20, 52, 420, 28);
        tbToken.Font = new Font("Consolas", 11f);
        tbToken.BackColor = Color.FromArgb(40, 42, 48);
        tbToken.ForeColor = Color.White;
        tbToken.BorderStyle = BorderStyle.FixedSingle;

        btnActivate = new Button();
        btnActivate.Text = "Activate";
        btnActivate.DialogResult = DialogResult.None;
        btnActivate.BackColor = Color.FromArgb(0, 120, 212);
        btnActivate.FlatStyle = FlatStyle.Flat;
        btnActivate.ForeColor = Color.White;
        btnActivate.FlatAppearance.BorderSize = 0;
        btnActivate.SetBounds(20, 95, 120, 34);
        btnActivate.Click += BtnActivate_Click;

        btnGetKey = new Button();
        btnGetKey.Text = "Get Free Token (Online)";
        btnGetKey.BackColor = Color.FromArgb(50, 53, 60);
        btnGetKey.FlatStyle = FlatStyle.Flat;
        btnGetKey.ForeColor = Color.Gainsboro;
        btnGetKey.FlatAppearance.BorderSize = 0;
        btnGetKey.SetBounds(150, 95, 180, 34);
        btnGetKey.Click += delegate {
            try { Process.Start(LicenseManager.PortalUrl); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open token website", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };

        lblStatus = new Label();
        lblStatus.Text = "Note: A token is 100% free and gives lifetime access for 1 PC.";
        lblStatus.ForeColor = Color.Gray;
        lblStatus.AutoSize = false;
        lblStatus.SetBounds(20, 145, 420, 40);

        Controls.AddRange(new Control[] { lbl, tbToken, btnActivate, btnGetKey, lblStatus });
    }

    void BtnActivate_Click(object sender, EventArgs e)
    {
        string tok = tbToken.Text.Trim();
        if (string.IsNullOrEmpty(tok))
        {
            MessageBox.Show(this, "Please enter a token.", "Missing Token", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnActivate.Enabled = false;
        btnActivate.Text = "Waiting...";
        lblStatus.Text = "Opening browser for secure verification...";
        lblStatus.ForeColor = Color.LightSkyBlue;
        Update();

        string msg;
        if (LicenseManager.VerifyOnlineViaBrowser(tok, out msg))
        {
            MessageBox.Show(this, "Activation successful! Lite Media Viewer is now unlocked for this PC.", "Activated", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            lblStatus.Text = msg;
            lblStatus.ForeColor = Color.FromArgb(255, 110, 110);
            btnActivate.Enabled = true;
            btnActivate.Text = "Activate";
        }
    }
}

// ====================================================================== VIEWER (with video / audio pane)
class Viewer : Form
{
    const string AppVersion = "1.0.0";
    const string UpdateFeedUrl = "https://lite-media-viewer.vercel.app/api/update";

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    const string Exts = ".jpg.jpeg.jfif.png.gif.bmp.tif.tiff.ico.";
    const string VExts = ".mp4.m4v.mov.wmv.avi.mpg.mpeg.mkv.webm.3gp.flv.asf.mp3.wav.wma.m4a.aac.flac.ogg.mid.";
    VideoPane vp; bool vid;
    List<string> files = new List<string>();
    int idx = -1;
    Image img; MemoryStream ms; string err; long curSize;
    float zoom = 1f, panX, panY; bool fit = true; int rot;
    bool dragging; Point lastPt;
    System.Windows.Forms.Timer slide = new System.Windows.Forms.Timer();
    bool full; FormBorderStyle prevStyle; FormWindowState prevState;

    List<Image> undo = new List<Image>(); List<int> undoRot = new List<int>(); bool dirty, dropped;
    bool cropMode, hasSel; RectangleF sel, s0; PointF cStart; int cdrag, cH; float ratio;

    ToolStrip tool = new ToolStrip();
    StatusStrip status = new StatusStrip();
    ContextMenuStrip menu = new ContextMenuStrip();
    List<ToolStripItem> imgItems = new List<ToolStripItem>(), navItems = new List<ToolStripItem>();
    ToolStripStatusLabel lName = new ToolStripStatusLabel(), lIdx = new ToolStripStatusLabel(), lDim = new ToolStripStatusLabel(), lSize = new ToolStripStatusLabel(), lZoom = new ToolStripStatusLabel();
    ToolStripSeparator sepCrop; ToolStripLabel lRatio; ToolStripComboBox cbRatio; ToolStripButton cropOk, cropNo, btnUpdate;

    static bool IsImage(string f) { return Exts.Contains(Path.GetExtension(f).ToLower() + "."); }
    static bool IsVideo(string f) { return VExts.Contains(Path.GetExtension(f).ToLower() + "."); }
    static bool IsMedia(string f) { return IsImage(f) || IsVideo(f); }
    static decimal Cl(decimal v, decimal min, decimal max) { return v < min ? min : (v > max ? max : v); }
    static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

    public Viewer(string path)
    {
        Text = "Lite Media Viewer";
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(26, 27, 30);
        ClientSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true; AllowDrop = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        slide.Interval = 3000;
        slide.Tick += delegate { if (!vid) Step(1); };
        BuildUi();
        vp = new VideoPane(); vp.FullHandler = ToggleFull; vp.EndedHandler = VideoEnded; Controls.Add(vp);
        DragEnter += DEnter; DragDrop += DDrop;
        tool.AllowDrop = true; tool.DragEnter += DEnter; tool.DragDrop += DDrop;
        status.AllowDrop = true; status.DragEnter += DEnter; status.DragDrop += DDrop;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            OpenPath(path, false);
            string p = files[0];
            Shown += delegate { ListFolder(p); };
        }
    }

    void DEnter(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
    void DDrop(object s, DragEventArgs e)
    {
        string[] d = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (d != null && d.Length > 0) OpenPath(d[0], true);
    }

    void Add(string name, string text, string tip, string keys, bool needImg, bool needNav, MethodInvoker act)
    {
        EventHandler h = delegate { act(); Focus(); };
        Image ic = Icons.Get(name);
        ToolStripMenuItem mi = new ToolStripMenuItem(text, ic, h);
        mi.Name = name; mi.ShortcutKeyDisplayString = keys; menu.Items.Add(mi);
        ToolStripButton b = new ToolStripButton(ic);
        b.Name = name; b.Click += h; b.DisplayStyle = ToolStripItemDisplayStyle.Image; b.AutoToolTip = false;
        b.ToolTipText = tip + (keys.Length > 0 ? "   (" + keys + ")" : "");
        b.Margin = new Padding(1, 2, 1, 2); b.Padding = new Padding(5);
        tool.Items.Add(b);
        if (needImg) { imgItems.Add(mi); imgItems.Add(b); }
        if (needNav) { navItems.Add(mi); navItems.Add(b); }
    }
    void Sep() { menu.Items.Add(new ToolStripSeparator()); ToolStripSeparator s = new ToolStripSeparator(); s.Margin = new Padding(5, 0, 5, 0); tool.Items.Add(s); }
    void RightLast() { tool.Items[tool.Items.Count - 1].Alignment = ToolStripItemAlignment.Right; }

    void BuildUi()
    {
        DarkRenderer rend = new DarkRenderer();
        tool.Renderer = rend; status.Renderer = rend; menu.Renderer = rend;
        tool.GripStyle = ToolStripGripStyle.Hidden; tool.Dock = DockStyle.Top; tool.TabStop = false;
        tool.AutoSize = false; tool.Height = 44; tool.ImageScalingSize = new Size(20, 20); tool.Padding = new Padding(8, 3, 8, 3);
        tool.BackColor = DarkRenderer.Bg; tool.ShowItemToolTips = true;
        menu.ImageScalingSize = new Size(20, 20); menu.BackColor = DarkRenderer.Bg; menu.ForeColor = Color.Gainsboro;

        Add("open", "Open...", "Open an image", "Ctrl+O", false, false, OpenDialog);
        Sep();
        Add("prev", "Previous", "Previous image", "Left", false, true, delegate { Step(-1); });
        Add("next", "Next", "Next image", "Right", false, true, delegate { Step(1); });
        Sep();
        Add("zout", "Zoom out", "Zoom out", "-", true, false, ZoomOut);
        Add("zin", "Zoom in", "Zoom in", "+", true, false, ZoomIn);
        Add("fit", "Fit to window", "Fit to window", "0", true, false, FitView);
        Add("actual", "Actual size (100%)", "Actual size: 1 image pixel = 1 screen pixel", "1", true, false, Actual);
        Sep();
        Add("rotl", "Rotate left", "Rotate left", "Shift+R", true, false, delegate { RotateBy(270); });
        Add("rotr", "Rotate right", "Rotate right", "R", true, false, delegate { RotateBy(90); });
        Add("fliph", "Flip horizontal", "Flip horizontally", "H", true, false, delegate { FlipBy(RotateFlipType.RotateNoneFlipX); });
        Add("flipv", "Flip vertical", "Flip vertically", "V", true, false, delegate { FlipBy(RotateFlipType.RotateNoneFlipY); });
        Sep();
        Add("crop", "Crop", "Crop: drag a box, then press Enter", "C", true, false, ToggleCrop);
        Add("resize", "Resize...", "Change size (pixels / percent) and resolution (DPI)", "Ctrl+E", true, false, ResizeDialog);
        Sep();
        Add("trim", "Trim video", "Trim video: keep only a part (needs ffmpeg.exe)", "T", false, false, delegate { OpenVideoEditor(false); });
        Add("vcrop", "Crop video", "Crop video: cut away the edges (needs ffmpeg.exe)", "C", false, false, delegate { OpenVideoEditor(true); });
        Sep();
        Add("undo", "Undo", "Undo last edit", "Ctrl+Z", true, false, Undo);
        Add("revert", "Revert to original", "Discard all edits and reload the file", "", true, false, Revert);
        Sep();
        Add("copy", "Copy", "Copy image to clipboard", "Ctrl+C", true, false, CopyImage);
        Add("save", "Save as...", "Save a copy (PNG / JPEG / BMP / TIFF)", "Ctrl+S", true, false, delegate { SaveAs(); });
        Add("folder", "Show in folder", "Show file in Explorer", "", true, false, ShowInFolder);
        Add("ext", "Default player", "Open this file with the program Windows uses by default", "", false, false, OpenExternal);
        Add("del", "Delete", "Move file to Recycle Bin", "Del", true, false, DeleteCurrent);

        sepCrop = new ToolStripSeparator(); sepCrop.Margin = new Padding(5, 0, 5, 0);
        lRatio = new ToolStripLabel("Ratio");
        cbRatio = new ToolStripComboBox(); cbRatio.DropDownStyle = ComboBoxStyle.DropDownList; cbRatio.Width = 72;
        cbRatio.FlatStyle = FlatStyle.Flat; cbRatio.BackColor = Color.FromArgb(52, 54, 60); cbRatio.ForeColor = Color.Gainsboro;
        cbRatio.Items.AddRange(new object[] { "Free", "1:1", "4:3", "3:2", "16:9", "9:16" }); cbRatio.SelectedIndex = 0;
        cbRatio.SelectedIndexChanged += delegate { SetRatio(); Focus(); };
        cropOk = new ToolStripButton("Apply", Icons.Get("ok")); cropOk.ToolTipText = "Apply crop (Enter)";
        cropOk.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText; cropOk.Padding = new Padding(4);
        cropOk.Click += delegate { ApplyCrop(); Focus(); };
        cropNo = new ToolStripButton("Cancel", Icons.Get("no")); cropNo.ToolTipText = "Cancel crop (Esc)";
        cropNo.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText; cropNo.Padding = new Padding(4);
        cropNo.Click += delegate { EndCrop(); Focus(); };
        foreach (ToolStripItem i in new ToolStripItem[] { sepCrop, lRatio, cbRatio, cropOk, cropNo }) { i.Visible = false; tool.Items.Add(i); }

        Add("slide", "Slideshow", "Start / stop slideshow (3 s per image)", "S", false, true, delegate { slide.Enabled = !slide.Enabled; UpdateUi(); }); RightLast();
        Add("full", "Fullscreen", "Fullscreen on / off", "F", false, false, ToggleFull); RightLast();
        Add("info", "Image details", "Image details: size, resolution, camera data", "I", true, false, ShowInfo); RightLast();
        Add("help", "Help", "Keyboard and mouse help", "F1", false, false, ShowHelp); RightLast();
        Add("update", "Check for updates", "Check online for a newer version", "", false, false, CheckForUpdates); RightLast();
        btnUpdate = tool.Items[tool.Items.Count - 1] as ToolStripButton;

        lName.Spring = true; lName.TextAlign = ContentAlignment.MiddleLeft;
        status.SizingGrip = false; status.BackColor = DarkRenderer.StatusBg; status.Padding = new Padding(8, 0, 8, 0);
        status.Items.AddRange(new ToolStripItem[] { lName, lIdx, lDim, lSize, lZoom });
        foreach (ToolStripStatusLabel l in new ToolStripStatusLabel[] { lIdx, lDim, lSize, lZoom }) { l.AutoSize = false; l.Width = 92; l.TextAlign = ContentAlignment.MiddleRight; }
        lDim.Width = 112;
        Controls.Add(status); Controls.Add(tool);
        ContextMenuStrip = menu;
        UpdateUi();
    }

    Rectangle View()
    {
        int t = tool.Visible ? tool.Height : 0, b = status.Visible ? status.Height : 0;
        return new Rectangle(0, t, ClientSize.Width, Math.Max(1, ClientSize.Height - t - b));
    }

    void SetEnabled(string name, bool en)
    {
        foreach (ToolStripItem i in tool.Items) if (i.Name == name) i.Enabled = en;
        foreach (ToolStripItem i in menu.Items) if (i.Name == name) i.Enabled = en;
    }

    void UpdateUi()
    {
        if (lRatio == null) return;
        bool has = img != null;
        foreach (ToolStripItem i in imgItems) i.Enabled = has;
        foreach (ToolStripItem i in navItems) i.Enabled = files.Count > 1;
        SetEnabled("undo", has && undo.Count > 0);
        SetEnabled("revert", has && dirty);
        foreach (ToolStripItem i in tool.Items) { ToolStripButton b = i as ToolStripButton; if (b != null) { if (b.Name == "slide") b.Checked = slide.Enabled; if (b.Name == "crop") b.Checked = cropMode; } }
        foreach (ToolStripItem i in menu.Items) { ToolStripMenuItem m = i as ToolStripMenuItem; if (m != null) { if (m.Name == "slide") m.Checked = slide.Enabled; if (m.Name == "crop") m.Checked = cropMode; } }
        sepCrop.Visible = lRatio.Visible = cbRatio.Visible = cropOk.Visible = cropNo.Visible = cropMode;
        SetEnabled("trim", vid); SetEnabled("vcrop", vid);
        if (vid && idx >= 0 && idx < files.Count)
        {
            string vn = Path.GetFileName(files[idx]);
            Text = vn + " - Lite Media Viewer"; lName.Text = vn; lIdx.Text = (idx + 1) + " / " + files.Count;
            lDim.Text = "Video / audio"; lZoom.Text = "";
            try { lSize.Text = Fmt(new FileInfo(files[idx]).Length); } catch { lSize.Text = ""; }
            SetEnabled("folder", true); SetEnabled("del", true); SetEnabled("info", true);
            return;
        }
        if (!has || idx < 0 || idx >= files.Count)
        {
            Text = "Lite Media Viewer";
            lName.Text = err == null ? "Click Open or drop an image here" : "";
            lIdx.Text = lDim.Text = lSize.Text = lZoom.Text = "";
            return;
        }
        string n = Path.GetFileName(files[idx]);
        Text = n + (dirty ? " *" : "") + " - Lite Media Viewer";
        if (cropMode && hasSel)
            lName.Text = "Crop: " + (int)Math.Round(sel.Width) + " x " + (int)Math.Round(sel.Height) + " at (" + (int)Math.Round(sel.X) + ", " + (int)Math.Round(sel.Y) + ")   -   Enter = apply,  Esc = cancel,  double-click inside = apply";
        else if (cropMode) lName.Text = "Crop: drag a box on the image";
        else lName.Text = n + (dirty ? "   (edited - not saved)" : "");
        lIdx.Text = (idx + 1) + " / " + files.Count;
        lDim.Text = img.Width + " x " + img.Height; lSize.Text = dirty ? "edited" : Fmt(curSize); lZoom.Text = (int)(Z() * 100) + "%";
    }
    static string Fmt(long n) { return n < 1024 ? n + " B" : n < 1048576 ? string.Format("{0:0} KB", n / 1024.0) : string.Format("{0:0.0} MB", n / 1048576.0); }

    static string JsonString(string json, string name)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            json, "\"" + System.Text.RegularExpressions.Regex.Escape(name) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        return match.Success ? match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
    }

    void CheckForUpdates()
    {
        if (btnUpdate != null) btnUpdate.Enabled = false;
        ThreadPool.QueueUserWorkItem(delegate
        {
            string latest = null, download = null, notes = null, error = null;
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
                catch (NotSupportedException) { }
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UpdateFeedUrl);
                request.Method = "GET";
                request.Accept = "application/json";
                request.UserAgent = "LiteMediaViewer/" + AppVersion;
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                string json;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    json = reader.ReadToEnd();

                latest = JsonString(json, "latestVersion");
                download = JsonString(json, "downloadUrl");
                notes = JsonString(json, "updateNotes");
                Version currentVersion, latestVersion;
                Uri downloadUri;
                if (!Version.TryParse(AppVersion, out currentVersion) ||
                    !Version.TryParse(latest, out latestVersion) ||
                    !Uri.TryCreate(download, UriKind.Absolute, out downloadUri) ||
                    downloadUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("The update service returned invalid version or download information.");
                if (latestVersion <= currentVersion) latest = null;
            }
            catch (Exception ex) { error = ex.Message; }

            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (btnUpdate != null) btnUpdate.Enabled = true;
                    if (error != null)
                    {
                        MessageBox.Show(this, "Could not check for updates.\n\n" + error, "Update check failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else if (latest == null)
                    {
                        MessageBox.Show(this, "You are using the latest version (" + AppVersion + ").", "No updates available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (MessageBox.Show(this,
                        "Version " + latest + " is available. You have version " + AppVersion + "." +
                        (string.IsNullOrEmpty(notes) ? "" : "\n\n" + notes) + "\n\nOpen the download page?",
                        "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        try { Process.Start(download); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open download", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    }
                }));
            }
            catch (InvalidOperationException) { }
        });
    }

    void OpenPath(string p, bool listNow)
    {
        if (!File.Exists(p)) return;
        if (dirty && !ConfirmDiscard()) return;
        string fp = Path.GetFullPath(p);
        files = new List<string>(); files.Add(fp);
        OpenIndex(0);
        if (listNow) ListFolder(fp);
    }

    void ListFolder(string current)
    {
        try
        {
            string dir = Path.GetDirectoryName(current);
            List<string> l = new List<string>();
            foreach (string f in Directory.GetFiles(dir)) if (IsMedia(f)) l.Add(f);
            l.Sort(delegate(string a, string b) { return StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)); });
            int k = l.FindIndex(delegate(string s) { return string.Equals(s, current, StringComparison.OrdinalIgnoreCase); });
            if (k < 0) { l.Insert(0, current); k = 0; }
            files = l; idx = k;
            UpdateUi(); Prefetch(idx + 1); Prefetch(idx - 1);
        }
        catch { }
    }

    void Unload()
    {
        cropMode = false; hasSel = false; cdrag = 0;
        if (img != null) { try { ImageAnimator.StopAnimate(img, OnFrame); } catch { } img.Dispose(); img = null; }
        foreach (Image u in undo) u.Dispose();
        undo.Clear(); undoRot.Clear(); dirty = false; dropped = false;
        if (ms != null) { ms.Dispose(); ms = null; }
    }

    void OnFrame(object s, EventArgs e) { if (IsHandleCreated) BeginInvoke(new MethodInvoker(Invalidate)); }

    static int ExifRot(Image i)
    {
        try
        {
            if (Array.IndexOf(i.PropertyIdList, 0x0112) < 0) return 0;
            int o = BitConverter.ToUInt16(i.GetPropertyItem(0x0112).Value, 0);
            return o == 3 ? 180 : o == 6 ? 90 : o == 8 ? 270 : 0;
        }
        catch { return 0; }
    }

    void OpenIndex(int i)
    {
        if (i < 0 || i >= files.Count) return;
        if (dirty && !ConfirmDiscard()) return;
        idx = i; Unload(); err = null;
        rot = 0; fit = true; zoom = 1f; panX = panY = 0;
        string p = files[i];
        if (IsVideo(p)) { StartVideo(p); return; }
        StopVideo();
        try
        {
            byte[] data = File.ReadAllBytes(p); curSize = data.Length;
            ms = new MemoryStream(data);
            img = Image.FromStream(ms, false, false);
            rot = ExifRot(img);
            if (ImageAnimator.CanAnimate(img)) ImageAnimator.Animate(img, OnFrame);
        }
        catch (Exception e)
        {
            Unload();
            err = "Cannot display: " + Path.GetFileName(p) + "\n(" + e.Message + ")";
        }
        UpdateUi(); Invalidate();
        Prefetch(i + 1); Prefetch(i - 1);
    }

    void Prefetch(int i)
    {
        if (i < 0 || i >= files.Count) return;
        string p = files[i];
        if (!IsImage(p)) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try { using (FileStream f = File.OpenRead(p)) { byte[] b = new byte[65536]; while (f.Read(b, 0, b.Length) > 0) { } } } catch { }
        });
    }

    void Step(int d)
    {
        if (files.Count < 2) return;
        int n = idx + d;
        if (n < 0) n = files.Count - 1; else if (n >= files.Count) n = 0;
        OpenIndex(n);
    }

    float FitZoom()
    {
        if (img == null) return 1f;
        bool side = rot % 180 != 0;
        float w = side ? img.Height : img.Width, h = side ? img.Width : img.Height;
        Rectangle v = View();
        return Math.Min(Math.Min(v.Width / w, v.Height / h), 1f);
    }
    float Z() { return fit ? FitZoom() : zoom; }

    void ZoomAt(float f, Point m)
    {
        if (img == null) return;
        float z = Z(), nz = Math.Max(0.02f, Math.Min(32f, z * f));
        Rectangle v = View();
        float vx = m.X - (v.X + v.Width / 2f), vy = m.Y - (v.Y + v.Height / 2f);
        panX = vx - (vx - panX) * nz / z; panY = vy - (vy - panY) * nz / z;
        zoom = nz; fit = false; Invalidate(); UpdateUi();
    }
    Point Center() { Rectangle v = View(); return new Point(v.X + v.Width / 2, v.Y + v.Height / 2); }
    void ZoomIn() { ZoomAt(1.25f, Center()); }
    void ZoomOut() { ZoomAt(1f / 1.25f, Center()); }
    void FitView() { fit = true; panX = panY = 0; Invalidate(); UpdateUi(); }
    void Actual() { panX = panY = 0; zoom = 1f; fit = false; Invalidate(); UpdateUi(); }
    void RotateBy(int d) { if (img == null || cropMode) return; rot = (rot + d) % 360; panX = panY = 0; Invalidate(); UpdateUi(); }

    PointF ToImg(Point s)
    {
        Rectangle v = View(); float z = Z();
        return new PointF((s.X - (v.X + v.Width / 2f + panX)) / z + img.Width / 2f, (s.Y - (v.Y + v.Height / 2f + panY)) / z + img.Height / 2f);
    }
    RectangleF ToScr(RectangleF r)
    {
        Rectangle v = View(); float z = Z();
        float ox = v.X + v.Width / 2f + panX - img.Width * z / 2f, oy = v.Y + v.Height / 2f + panY - img.Height * z / 2f;
        return new RectangleF(ox + r.X * z, oy + r.Y * z, r.Width * z, r.Height * z);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        if (vid) return;
        Rectangle vr = View();
        if (img == null)
        {
            string m = err ?? "Drop an image here, or click Open";
            string sub = err == null ? "Press F1 for shortcuts" : "";
            int cx = vr.X + vr.Width / 2, cy = vr.Y + vr.Height / 2;
            using (Bitmap big = Icons.Make(err == null ? "open" : "no", 72, Color.FromArgb(95, 102, 114)))
                g.DrawImage(big, cx - 36, cy - 90, 72, 72);
            using (StringFormat sf = new StringFormat())
            using (Font f1 = new Font(Font.FontFamily, 13f, FontStyle.Regular))
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Near;
                g.DrawString(m, f1, Brushes.Gainsboro, new RectangleF(vr.X, cy - 6, vr.Width, 60), sf);
                g.DrawString(sub, Font, Brushes.Gray, new RectangleF(vr.X, cy + 28, vr.Width, 30), sf);
            }
            return;
        }
        if (ImageAnimator.CanAnimate(img)) ImageAnimator.UpdateFrames(img);
        float z = Z();
        g.SetClip(vr);
        g.InterpolationMode = dragging ? InterpolationMode.Bilinear : (z < 1f ? InterpolationMode.HighQualityBicubic : (z >= 3f ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear));
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TranslateTransform(vr.X + vr.Width / 2f + panX, vr.Y + vr.Height / 2f + panY);
        g.RotateTransform(rot);
        g.ScaleTransform(z, z);
        float iw = img.Width, ih = img.Height;
        using (SolidBrush sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(sh, -iw / 2f + 4 / z, -ih / 2f + 6 / z, iw, ih);
        if (Image.IsAlphaPixelFormat(img.PixelFormat))
            using (HatchBrush hb = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(78, 78, 84), Color.FromArgb(58, 58, 64))) g.FillRectangle(hb, -iw / 2f, -ih / 2f, iw, ih);
        g.DrawImage(img, -iw / 2f, -ih / 2f, iw, ih);

        if (cropMode && hasSel)
        {
            g.ResetTransform(); g.SetClip(vr);
            RectangleF rs = ToScr(sel); Rectangle ri = Rectangle.Round(rs);
            g.ExcludeClip(ri);
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(dim, vr);
            g.SetClip(vr);
            using (Pen p1 = new Pen(Color.Black, 3), p2 = new Pen(Color.White, 1), p3 = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
            {
                g.DrawRectangle(p1, ri.X, ri.Y, ri.Width, ri.Height);
                g.DrawRectangle(p2, ri.X, ri.Y, ri.Width, ri.Height);
                for (int k = 1; k < 3; k++)
                {
                    float gx = rs.X + rs.Width * k / 3f, gy = rs.Y + rs.Height * k / 3f;
                    g.DrawLine(p3, gx, rs.Y, gx, rs.Bottom); g.DrawLine(p3, rs.X, gy, rs.Right, gy);
                }
            }
            foreach (PointF hp in HandlePts(rs))
            {
                g.FillRectangle(Brushes.White, hp.X - 4, hp.Y - 4, 8, 8);
                g.DrawRectangle(Pens.Black, hp.X - 4, hp.Y - 4, 8, 8);
            }
            string t = (int)Math.Round(sel.Width) + " x " + (int)Math.Round(sel.Height);
            SizeF ts = g.MeasureString(t, Font);
            float ty = Math.Max(vr.Y, rs.Y - ts.Height - 2);
            g.FillRectangle(Brushes.Black, rs.X, ty, ts.Width, ts.Height);
            g.DrawString(t, Font, Brushes.White, rs.X, ty);
        }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutVideo(); UpdateUi(); }

    protected override void OnMouseWheel(MouseEventArgs e) { if (vid) { vp.VolumeBy(e.Delta > 0 ? 5 : -5); return; } ZoomAt(e.Delta > 0 ? 1.2f : 1f / 1.2f, e.Location); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (img == null) return;
        if (cropMode && e.Button == MouseButtons.Left) { Focus(); CropDown(e.Location); return; }
        if (e.Button == MouseButtons.Left || (cropMode && e.Button == MouseButtons.Middle))
        { Focus(); dragging = true; lastPt = e.Location; Cursor = Cursors.SizeAll; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (cropMode && cdrag != 0) { CropMove(e.Location); return; }
        if (!dragging) { if (cropMode && img != null) CropCursor(e.Location); return; }
        panX += e.X - lastPt.X; panY += e.Y - lastPt.Y; lastPt = e.Location; Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.XButton1) Step(-1); else if (e.Button == MouseButtons.XButton2) Step(1);
        if (cdrag != 0) { cdrag = 0; if (sel.Width < 3 || sel.Height < 3) hasSel = false; UpdateUi(); Invalidate(); }
        dragging = false; Cursor = Cursors.Default; Invalidate();
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (cropMode) { if (hasSel && ToScr(sel).Contains(e.Location)) ApplyCrop(); return; }
        panX = panY = 0;
        if (fit) { zoom = 1f; fit = false; } else fit = true;
        Invalidate(); UpdateUi();
    }

    PointF[] HandlePts(RectangleF r)
    {
        float mx = r.X + r.Width / 2f, my = r.Y + r.Height / 2f;
        return new PointF[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom),
                              new PointF(mx, r.Top), new PointF(r.Right, my), new PointF(mx, r.Bottom), new PointF(r.Left, my) };
    }
    int CropHit(Point s)
    {
        if (!hasSel) return -1;
        RectangleF rs = ToScr(sel); PointF[] h = HandlePts(rs);
        for (int i = 0; i < 8; i++) if (Math.Abs(s.X - h[i].X) <= 7 && Math.Abs(s.Y - h[i].Y) <= 7) return i;
        return rs.Contains(s.X, s.Y) ? 8 : -1;
    }
    void CropCursor(Point s)
    {
        int h = CropHit(s);
        Cursor = (h == 0 || h == 2) ? Cursors.SizeNWSE : (h == 1 || h == 3) ? Cursors.SizeNESW : (h == 4 || h == 6) ? Cursors.SizeNS : (h == 5 || h == 7) ? Cursors.SizeWE : h == 8 ? Cursors.SizeAll : Cursors.Cross;
    }
    PointF ClampPt(PointF p) { return new PointF(Math.Max(0, Math.Min(img.Width, p.X)), Math.Max(0, Math.Min(img.Height, p.Y))); }
    static RectangleF Norm(float x1, float y1, float x2, float y2) { return RectangleF.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)); }

    RectangleF Constrain(PointF a, PointF p)
    {
        float w = p.X - a.X, h = p.Y - a.Y;
        if (ratio > 0)
        {
            float aw = Math.Abs(w), ah = Math.Abs(h), sx = w < 0 ? -1 : 1, sy = h < 0 ? -1 : 1;
            if (aw / ratio > ah) aw = ah * ratio; else ah = aw / ratio;
            float maxW = sx > 0 ? img.Width - a.X : a.X, maxH = sy > 0 ? img.Height - a.Y : a.Y;
            if (aw > maxW) { aw = maxW; ah = aw / ratio; }
            if (ah > maxH) { ah = maxH; aw = ah * ratio; }
            w = sx * aw; h = sy * ah;
        }
        return Norm(a.X, a.Y, a.X + w, a.Y + h);
    }

    void CropDown(Point s)
    {
        PointF p = ClampPt(ToImg(s));
        int h = CropHit(s);
        if (h >= 0 && h < 8) { cdrag = 3; cH = h; s0 = sel; }
        else if (h == 8) { cdrag = 2; s0 = sel; cStart = p; }
        else { cdrag = 1; cStart = p; sel = new RectangleF(p.X, p.Y, 0, 0); hasSel = true; }
    }

    void CropMove(Point s)
    {
        PointF p = ClampPt(ToImg(s));
        if (cdrag == 1) sel = Constrain(cStart, p);
        else if (cdrag == 2)
        {
            float nx = Math.Max(0, Math.Min(img.Width - s0.Width, s0.X + p.X - cStart.X));
            float ny = Math.Max(0, Math.Min(img.Height - s0.Height, s0.Y + p.Y - cStart.Y));
            sel = new RectangleF(nx, ny, s0.Width, s0.Height);
        }
        else if (cdrag == 3)
        {
            switch (cH)
            {
                case 0: sel = Constrain(new PointF(s0.Right, s0.Bottom), p); break;
                case 1: sel = Constrain(new PointF(s0.Left, s0.Bottom), p); break;
                case 2: sel = Constrain(new PointF(s0.Left, s0.Top), p); break;
                case 3: sel = Constrain(new PointF(s0.Right, s0.Top), p); break;
                case 4: sel = RectangleF.FromLTRB(s0.Left, Math.Min(p.Y, s0.Bottom - 1), s0.Right, s0.Bottom); break;
                case 5: sel = RectangleF.FromLTRB(s0.Left, s0.Top, Math.Max(p.X, s0.Left + 1), s0.Bottom); break;
                case 6: sel = RectangleF.FromLTRB(s0.Left, s0.Top, s0.Right, Math.Max(p.Y, s0.Top + 1)); break;
                default: sel = RectangleF.FromLTRB(Math.Min(p.X, s0.Right - 1), s0.Top, s0.Right, s0.Bottom); break;
            }
        }
        Invalidate(); UpdateUi();
    }

    void SetRatio()
    {
        string t = cbRatio.Text; ratio = 0;
        int k = t.IndexOf(':');
        if (k > 0) { float a, b; if (float.TryParse(t.Substring(0, k), out a) && float.TryParse(t.Substring(k + 1), out b) && b > 0) ratio = a / b; }
        if (cropMode && hasSel && ratio > 0 && img != null)
        {
            float cx = sel.X + sel.Width / 2f, cy = sel.Y + sel.Height / 2f, w = sel.Width, h = w / ratio;
            if (h > sel.Height) { h = sel.Height; w = h * ratio; }
            sel = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
            Invalidate(); UpdateUi();
        }
    }

    void ToggleCrop()
    {
        if (cropMode) { EndCrop(); return; }
        if (img == null) return;
        if (rot != 0) ApplyBitmap(Rotated());
        fit = true; panX = panY = 0;
        cropMode = true; hasSel = true; cdrag = 0;
        sel = new RectangleF(img.Width * 0.1f, img.Height * 0.1f, img.Width * 0.8f, img.Height * 0.8f);
        SetRatio(); Cursor = Cursors.Cross; Invalidate(); UpdateUi();
    }
    void EndCrop() { cropMode = false; hasSel = false; cdrag = 0; Cursor = Cursors.Default; Invalidate(); UpdateUi(); }

    void ApplyCrop()
    {
        if (!cropMode || !hasSel || img == null) return;
        Rectangle r = Rectangle.Intersect(Rectangle.Round(sel), new Rectangle(0, 0, img.Width, img.Height));
        if (r.Width < 1 || r.Height < 1) return;
        try
        {
            Bitmap b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            b.SetResolution(img.HorizontalResolution, img.VerticalResolution);
            using (Graphics g = Graphics.FromImage(b)) g.DrawImage(img, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            cropMode = false; hasSel = false; Cursor = Cursors.Default;
            ApplyBitmap(b);
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Crop failed"); }
    }

    void ApplyBitmap(Bitmap b)
    {
        if (img != null)
        {
            try { ImageAnimator.StopAnimate(img, OnFrame); } catch { }
            undo.Add(img); undoRot.Add(rot);
            while (undo.Count > 3) { undo[0].Dispose(); undo.RemoveAt(0); undoRot.RemoveAt(0); dropped = true; }
        }
        img = b; rot = 0; dirty = true; panX = panY = 0; fit = true;
        Invalidate(); UpdateUi();
    }

    void Undo()
    {
        EndCrop();
        if (undo.Count == 0) return;
        int k = undo.Count - 1; Image old = img;
        img = undo[k]; rot = undoRot[k]; undo.RemoveAt(k); undoRot.RemoveAt(k);
        if (old != null) old.Dispose();
        dirty = undo.Count > 0 || dropped; panX = panY = 0; fit = true;
        Invalidate(); UpdateUi();
    }

    void Revert()
    {
        if (idx < 0) return;
        if (dirty && MessageBox.Show(this, "Discard all edits and reload the original file?", "Revert", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        dirty = false; OpenIndex(idx);
    }

    Bitmap Rotated()
    {
        Bitmap b = new Bitmap(img);
        RotateFlipType t = rot == 90 ? RotateFlipType.Rotate90FlipNone : rot == 180 ? RotateFlipType.Rotate180FlipNone : rot == 270 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone;
        if (t != RotateFlipType.RotateNoneFlipNone) b.RotateFlip(t);
        return b;
    }

    void FlipBy(RotateFlipType t)
    {
        if (img == null) return;
        EndCrop();
        try { Bitmap b = Rotated(); b.RotateFlip(t); ApplyBitmap(b); }
        catch (Exception e) { MessageBox.Show(this, e.Message); }
    }

    void ResizeDialog()
    {
        if (img == null) return;
        EndCrop();
        bool side = rot % 180 != 0;
        int ow = side ? img.Height : img.Width, oh = side ? img.Width : img.Height;
        decimal odpi = img.HorizontalResolution > 1 ? Math.Round((decimal)img.HorizontalResolution) : 72;
        bool busy = false;
        using (Form f = new Form())
        {
            f.Text = "Resize image"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false; f.ClientSize = new Size(330, 270);
            Label info = new Label(); info.Text = "Original: " + ow + " x " + oh + " px  (" + (ow * (long)oh / 1000000.0).ToString("0.0") + " MP, " + odpi + " DPI)"; info.SetBounds(12, 10, 310, 20);
            Label l0 = new Label(); l0.Text = "Preset"; l0.SetBounds(12, 42, 95, 20);
            Label l1 = new Label(); l1.Text = "Width (px)"; l1.SetBounds(12, 72, 95, 20);
            Label l2 = new Label(); l2.Text = "Height (px)"; l2.SetBounds(12, 102, 95, 20);
            Label l3 = new Label(); l3.Text = "Percent (%)"; l3.SetBounds(12, 132, 95, 20);
            Label l4 = new Label(); l4.Text = "Resolution (DPI)"; l4.SetBounds(12, 162, 95, 20);
            ComboBox cp = new ComboBox(); cp.DropDownStyle = ComboBoxStyle.DropDownList; cp.SetBounds(112, 38, 205, 22);
            cp.Items.AddRange(new object[] { "Custom", "Web - 1920 px wide", "HD - 1280 px wide", "Email - 800 px wide", "Thumbnail - 640 px wide" }); cp.SelectedIndex = 0;
            NumericUpDown nw = new NumericUpDown(), nh = new NumericUpDown(), np = new NumericUpDown(), nd = new NumericUpDown();
            nw.Minimum = 1; nw.Maximum = 30000; nw.Value = ow; nw.SetBounds(112, 69, 90, 22);
            nh.Minimum = 1; nh.Maximum = 30000; nh.Value = oh; nh.SetBounds(112, 99, 90, 22);
            np.Minimum = 1; np.Maximum = 1000; np.Value = 100; np.SetBounds(112, 129, 90, 22);
            nd.Minimum = 1; nd.Maximum = 2400; nd.Value = Cl(odpi, 1, 2400); nd.SetBounds(112, 159, 90, 22);
            CheckBox keep = new CheckBox(); keep.Text = "Keep aspect ratio"; keep.Checked = true; keep.SetBounds(112, 192, 200, 22);
            Button ok = new Button(); ok.Text = "Resize"; ok.DialogResult = DialogResult.OK; ok.SetBounds(150, 230, 80, 28);
            Button no = new Button(); no.Text = "Cancel"; no.DialogResult = DialogResult.Cancel; no.SetBounds(240, 230, 80, 28);
            f.AcceptButton = ok; f.CancelButton = no;
            nw.ValueChanged += delegate
            {
                if (busy) return; busy = true;
                if (keep.Checked) nh.Value = Cl(Math.Round(nw.Value * oh / ow), 1, 30000);
                np.Value = Cl(Math.Round(nw.Value * 100 / ow), 1, 1000); busy = false;
            };
            nh.ValueChanged += delegate
            {
                if (busy) return; busy = true;
                if (keep.Checked) nw.Value = Cl(Math.Round(nh.Value * ow / oh), 1, 30000);
                np.Value = Cl(Math.Round(nh.Value * 100 / oh), 1, 1000); busy = false;
            };
            np.ValueChanged += delegate
            {
                if (busy) return; busy = true;
                nw.Value = Cl(Math.Round(np.Value * ow / 100), 1, 30000); nh.Value = Cl(Math.Round(np.Value * oh / 100), 1, 30000); busy = false;
            };
            cp.SelectedIndexChanged += delegate
            {
                string t = cp.Text; int tw = t.Contains("1920") ? 1920 : t.Contains("1280") ? 1280 : t.Contains("800") ? 800 : t.Contains("640") ? 640 : 0;
                if (tw > 0) { keep.Checked = true; nw.Value = tw; }
            };
            f.Controls.AddRange(new Control[] { info, l0, l1, l2, l3, l4, cp, nw, nh, np, nd, keep, ok, no });
            if (f.ShowDialog(this) != DialogResult.OK) return;
            int w = (int)nw.Value, h = (int)nh.Value; float dpi = (float)nd.Value;
            if (w == ow && h == oh && Math.Abs(dpi - (float)odpi) < 0.5f) return;
            if ((long)w * h > 120000000L) { MessageBox.Show(this, "That size is too large for this PC's memory (over 120 megapixels).", "Resize"); return; }
            try
            {
                using (Bitmap src = Rotated())
                {
                    Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(b))
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality; g.SmoothingMode = SmoothingMode.HighQuality;
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                    }
                    b.SetResolution(dpi, dpi);
                    ApplyBitmap(b);
                }
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Resize failed"); }
        }
    }

    string ExifStr(int id)
    {
        try
        {
            if (Array.IndexOf(img.PropertyIdList, id) < 0) return null;
            PropertyItem pi = img.GetPropertyItem(id);
            if (pi.Type == 2) return Encoding.ASCII.GetString(pi.Value).Trim('\0', ' ');
            if (pi.Type == 3) return BitConverter.ToUInt16(pi.Value, 0).ToString();
            if (pi.Type == 4) return BitConverter.ToUInt32(pi.Value, 0).ToString();
        }
        catch { }
        return null;
    }
    double ExifRat(int id)
    {
        try
        {
            if (Array.IndexOf(img.PropertyIdList, id) < 0) return double.NaN;
            byte[] v = img.GetPropertyItem(id).Value;
            uint n = BitConverter.ToUInt32(v, 0), d = BitConverter.ToUInt32(v, 4);
            return d == 0 ? double.NaN : (double)n / d;
        }
        catch { return double.NaN; }
    }
    static string FormatName(Image i)
    {
        Guid g = i.RawFormat.Guid;
        if (g == ImageFormat.Jpeg.Guid) return "JPEG"; if (g == ImageFormat.Png.Guid) return "PNG"; if (g == ImageFormat.Gif.Guid) return "GIF";
        if (g == ImageFormat.Bmp.Guid) return "BMP"; if (g == ImageFormat.Tiff.Guid) return "TIFF"; if (g == ImageFormat.Icon.Guid) return "ICO";
        return "Edited bitmap";
    }
    void ShowInfo()
    {
        if (vid && idx >= 0 && idx < files.Count) { ShowVideoInfo(); return; }
        if (img == null || idx < 0) return;
        string p = files[idx]; StringBuilder sb = new StringBuilder();
        sb.AppendLine("FILE");
        sb.AppendLine("  Name         : " + Path.GetFileName(p));
        sb.AppendLine("  Folder       : " + Path.GetDirectoryName(p));
        try
        {
            FileInfo fi = new FileInfo(p);
            sb.AppendLine("  File size    : " + Fmt(fi.Length) + "  (" + fi.Length.ToString("N0") + " bytes)");
            sb.AppendLine("  Created      : " + fi.CreationTime);
            sb.AppendLine("  Modified     : " + fi.LastWriteTime);
        }
        catch { }
        sb.AppendLine(); sb.AppendLine("IMAGE" + (dirty ? "   (edited, not saved yet)" : ""));
        int g = Gcd(img.Width, img.Height);
        sb.AppendLine("  Format       : " + FormatName(img));
        sb.AppendLine("  Resolution   : " + img.Width + " x " + img.Height + " px   (" + (img.Width * (long)img.Height / 1000000.0).ToString("0.0") + " megapixels)");
        sb.AppendLine("  Aspect ratio : " + (img.Width / g) + ":" + (img.Height / g));
        sb.AppendLine("  DPI          : " + Math.Round(img.HorizontalResolution) + " x " + Math.Round(img.VerticalResolution));
        sb.AppendLine("  Color depth  : " + Image.GetPixelFormatSize(img.PixelFormat) + " bit");
        if (img.HorizontalResolution > 1 && img.VerticalResolution > 1)
        {
            double wi = img.Width / img.HorizontalResolution, hi = img.Height / img.VerticalResolution;
            sb.AppendLine("  Print size   : " + wi.ToString("0.0") + " x " + hi.ToString("0.0") + " in   (" + (wi * 2.54).ToString("0.0") + " x " + (hi * 2.54).ToString("0.0") + " cm)");
        }
        string make = ExifStr(0x010F), model = ExifStr(0x0110), date = ExifStr(0x9003), iso = ExifStr(0x8827), soft = ExifStr(0x0131), lens = ExifStr(0xA434);
        double ex = ExifRat(0x829A), fn = ExifRat(0x829D), fl = ExifRat(0x920A);
        if (make != null || model != null || date != null || !double.IsNaN(ex) || !double.IsNaN(fn) || iso != null)
        {
            sb.AppendLine(); sb.AppendLine("CAMERA / EXIF");
            if (make != null || model != null) sb.AppendLine("  Camera       : " + (make + " " + model).Trim());
            if (lens != null) sb.AppendLine("  Lens         : " + lens);
            if (date != null) sb.AppendLine("  Date taken   : " + date);
            if (!double.IsNaN(ex)) sb.AppendLine("  Exposure     : " + (ex > 0 && ex < 1 ? "1/" + Math.Round(1 / ex) + " s" : ex.ToString("0.###") + " s"));
            if (!double.IsNaN(fn)) sb.AppendLine("  Aperture     : f/" + fn.ToString("0.#"));
            if (iso != null) sb.AppendLine("  ISO          : " + iso);
            if (!double.IsNaN(fl)) sb.AppendLine("  Focal length : " + fl.ToString("0.#") + " mm");
            if (soft != null) sb.AppendLine("  Software     : " + soft);
        }
        string text = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        using (Form f = new Form())
        {
            f.Text = "Image details"; f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(560, 440); f.ShowInTaskbar = false;
            f.MinimizeBox = false; f.MaximizeBox = false; f.FormBorderStyle = FormBorderStyle.FixedDialog;
            TextBox tb = new TextBox(); tb.Multiline = true; tb.ReadOnly = true; tb.ScrollBars = ScrollBars.Vertical; tb.Font = new Font("Consolas", 9.5f);
            tb.Text = text; tb.SetBounds(8, 8, 544, 385); tb.BackColor = Color.White; tb.Select(0, 0);
            Button cp = new Button(); cp.Text = "Copy"; cp.SetBounds(380, 402, 80, 28); cp.Click += delegate { try { Clipboard.SetText(text); } catch { } };
            Button cl = new Button(); cl.Text = "Close"; cl.SetBounds(470, 402, 80, 28); cl.DialogResult = DialogResult.Cancel;
            f.CancelButton = cl; f.Controls.AddRange(new Control[] { tb, cp, cl });
            f.ShowDialog(this);
        }
    }

    void ShowHelp()
    {
        MessageBox.Show(this,
            "VIDEO / AUDIO  (Windows Media Player engine)\n  Space - play / pause      Up / Down - volume +/-      M - mute\n  Left / Right - seek 5 s (Shift = 30 s)      Mouse wheel - volume\n  PageUp / PageDown - previous / next file      F - fullscreen\n  T - trim video      C - crop video      (needs ffmpeg.exe next to the program)\n\n" +
            "BROWSE PHOTOS\n  Left / Right, Space  - previous / next     Home / End  - first / last\n  Mouse back / forward buttons also work\n\n" +
            "ZOOM & MOVE\n  Mouse wheel - zoom at cursor      Drag - move image\n  Double-click - fit / 100%      + / -  zoom,  0 fit,  1 actual size\n\n" +
            "EDIT  (original file is never changed until you Save as)\n  R / Shift+R - rotate      H / V - flip\n  C - crop: drag a box, move or resize it, choose a ratio,\n      press Enter (or double-click inside) to apply, Esc to cancel\n  Ctrl+E - resize (pixels, percent, DPI)\n  Ctrl+Z - undo      Ctrl+C - copy      Ctrl+S - save a copy\n\n" +
            "OTHER\n  I - details (size, resolution, camera)      F / F11 - fullscreen\n  S - slideshow      Delete - Recycle Bin      Ctrl+O - open\n  Right-click - menu.  Drag and drop an image onto the window to open it.",
            "Lite Media Viewer - Help");
    }

    void OpenDialog()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Filter = "Photos and videos|*.jpg;*.jpeg;*.jfif;*.png;*.gif;*.bmp;*.tif;*.tiff;*.ico;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mpg;*.mpeg;*.mkv;*.webm;*.3gp;*.flv;*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.ogg|Images|*.jpg;*.jpeg;*.jfif;*.png;*.gif;*.bmp;*.tif;*.tiff;*.ico|All files|*.*";
            if (d.ShowDialog(this) == DialogResult.OK) OpenPath(d.FileName, true);
        }
    }
    void CopyImage() { if (img == null) return; try { Clipboard.SetImage(Rotated()); lName.Text = "Copied to clipboard"; } catch (Exception e) { MessageBox.Show(this, e.Message); } }

    bool SaveAs()
    {
        if (img == null || idx < 0) return false;
        using (SaveFileDialog d = new SaveFileDialog())
        {
            d.Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp|TIFF|*.tif";
            string oe = Path.GetExtension(files[idx]).ToLower();
            d.FilterIndex = (oe == ".jpg" || oe == ".jpeg" || oe == ".jfif") ? 2 : oe == ".bmp" ? 3 : (oe == ".tif" || oe == ".tiff") ? 4 : 1;
            d.FileName = Path.GetFileNameWithoutExtension(files[idx]) + "_edit";
            d.InitialDirectory = Path.GetDirectoryName(files[idx]);
            if (d.ShowDialog(this) != DialogResult.OK) return false;
            try
            {
                string x = Path.GetExtension(d.FileName).ToLower();
                using (Bitmap b = Rotated())
                {
                    if (x == ".jpg" || x == ".jpeg")
                    {
                        ImageCodecInfo jc = null;
                        foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.FormatID == ImageFormat.Jpeg.Guid) jc = c;
                        EncoderParameters ep = new EncoderParameters(1);
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        b.Save(d.FileName, jc, ep);
                    }
                    else b.Save(d.FileName, x == ".bmp" ? ImageFormat.Bmp : (x == ".tif" || x == ".tiff") ? ImageFormat.Tiff : ImageFormat.Png);
                }
                dirty = false; UpdateUi();
                lName.Text = "Saved: " + Path.GetFileName(d.FileName);
                return true;
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Save failed"); return false; }
        }
    }

    bool ConfirmDiscard()
    {
        DialogResult r = MessageBox.Show(this, "You have unsaved edits.\nSave a copy before continuing?", "Unsaved edits", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes && !SaveAs()) return false;
        dirty = false; return true;
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (dirty && !ConfirmDiscard()) e.Cancel = true;
        if (!e.Cancel && vp != null) { try { vp.Close(); } catch { } }
        base.OnFormClosing(e);
    }

    void ShowInFolder()
    {
        if (idx < 0 || idx >= files.Count) return;
        try { Process.Start("explorer.exe", "/select,\"" + files[idx] + "\""); } catch { }
    }

    void DeleteCurrent()
    {
        if (idx < 0 || idx >= files.Count) return;
        string p = files[idx];
        if (MessageBox.Show(this, "Move to Recycle Bin?\n" + Path.GetFileName(p), "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        bool wasVideo = vid;
        dirty = false; Unload(); StopVideo();
        if (wasVideo) { Application.DoEvents(); Thread.Sleep(300); }   // let Windows Media Player let go of the file
        try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); } catch { }
        if (File.Exists(p))
        {
            MessageBox.Show(this, "The file could not be deleted (is another program using it?).", "Delete");
            OpenIndex(idx); return;
        }
        files.RemoveAt(idx);
        if (files.Count == 0) { idx = -1; err = "No more images in this folder."; UpdateUi(); Invalidate(); return; }
        OpenIndex(Math.Min(idx, files.Count - 1));
    }

    void ToggleFull()
    {
        if (!full) { prevStyle = FormBorderStyle; prevState = WindowState; FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal; WindowState = FormWindowState.Maximized; full = true; tool.Visible = false; status.Visible = false; }
        else { FormBorderStyle = prevStyle; WindowState = prevState; full = false; tool.Visible = true; status.Visible = true; }
        LayoutVideo(); Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool h = true;
        switch (e.KeyCode)
        {
            case Keys.Right: case Keys.PageDown: case Keys.Space: Step(1); break;
            case Keys.Left: case Keys.PageUp: case Keys.Back: Step(-1); break;
            case Keys.Home: OpenIndex(0); break;
            case Keys.End: OpenIndex(files.Count - 1); break;
            case Keys.Add: case Keys.Oemplus: ZoomIn(); break;
            case Keys.Subtract: case Keys.OemMinus: ZoomOut(); break;
            case Keys.D0: case Keys.NumPad0: FitView(); break;
            case Keys.D1: case Keys.NumPad1: Actual(); break;
            case Keys.R: RotateBy(e.Shift ? 270 : 90); break;
            case Keys.H: FlipBy(RotateFlipType.RotateNoneFlipX); break;
            case Keys.V: FlipBy(RotateFlipType.RotateNoneFlipY); break;
            case Keys.I: ShowInfo(); break;
            case Keys.F: case Keys.F11: ToggleFull(); break;
            case Keys.F1: ShowHelp(); break;
            case Keys.S: if (e.Control) SaveAs(); else { slide.Enabled = !slide.Enabled; UpdateUi(); } break;
            case Keys.Escape: if (cropMode) EndCrop(); else if (slide.Enabled) { slide.Enabled = false; UpdateUi(); } else if (full) ToggleFull(); break;
            case Keys.Enter: if (cropMode) ApplyCrop(); else h = false; break;
            case Keys.Delete: DeleteCurrent(); break;
            case Keys.O: if (e.Control) OpenDialog(); else h = false; break;
            case Keys.C: if (e.Control) CopyImage(); else ToggleCrop(); break;
            case Keys.Z: if (e.Control) Undo(); else h = false; break;
            case Keys.E: if (e.Control) ResizeDialog(); else h = false; break;
            default: h = false; break;
        }
        if (h) e.Handled = true; else base.OnKeyDown(e);
    }

    void LayoutVideo()
    {
        if (vp == null || !vid) return;
        vp.Bounds = View();
        if (!vp.Visible) vp.Visible = true;
        vp.BringToFront();
    }
    void StartVideo(string p) { vid = true; LayoutVideo(); vp.Open(p); UpdateUi(); Invalidate(); }
    void StopVideo() { if (!vid) return; vid = false; vp.Close(); UpdateUi(); }
    void VideoEnded() { if (slide.Enabled && files.Count > 1) Step(1); }
    void OpenVideoEditor(bool crop)
    {
        if (!vid || idx < 0 || idx >= files.Count) return;
        string ff = Ffmpeg.Find();
        if (ff == null)
        {
            if (MessageBox.Show(this,
                "Trim and crop need the free program ffmpeg.\n\n" +
                "1.  Download  \"ffmpeg-release-essentials.zip\"  from  gyan.dev/ffmpeg/builds\n" +
                "2.  Copy  ffmpeg.exe  (it is in the bin folder of the zip) next to LiteMediaViewer.exe\n\n" +
                "(If ffmpeg does not start on Windows 7, download an older version, 5.x or 6.0.)\n\n" +
                "Open the program folder now?", "ffmpeg not found", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            { try { Process.Start("explorer.exe", "\"" + AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + "\""); } catch { } }
            return;
        }
        string path = files[idx];
        double pos = vp.Position(); vp.Pause();
        double dur; bool hv, ha;
        Cursor = Cursors.WaitCursor;
        try { Ffmpeg.Probe(ff, path, out dur, out hv, out ha); }
        catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show(this, "ffmpeg could not be started:\n" + ex.Message, "Edit video"); return; }
        Cursor = Cursors.Default;
        if (dur <= 0 || (!hv && !ha)) { MessageBox.Show(this, "ffmpeg could not read this file.", "Edit video"); return; }
        using (VideoEditDialog d = new VideoEditDialog(ff, path, dur, hv, ha, pos, crop && hv))
        {
            d.ShowDialog(this);
            if (d.SavedPath != null) ListFolder(path);   // show the new file in the list
        }
    }
    void OpenExternal() { if (idx < 0 || idx >= files.Count) return; try { Process.Start(files[idx]); } catch { } }
    void ShowVideoInfo()
    {
        string p = files[idx]; StringBuilder sb = new StringBuilder();
        sb.AppendLine("FILE");
        sb.AppendLine("  Name         : " + Path.GetFileName(p));
        sb.AppendLine("  Folder       : " + Path.GetDirectoryName(p));
        try { FileInfo fi = new FileInfo(p); sb.AppendLine("  File size    : " + Fmt(fi.Length)); sb.AppendLine("  Modified     : " + fi.LastWriteTime); } catch { }
        sb.AppendLine(); sb.AppendLine("PLAYBACK"); sb.AppendLine(vp.InfoText());
        MessageBox.Show(this, sb.ToString(), "Media details");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (vid && (keyData & (Keys.Control | Keys.Alt)) == 0)
        {
            bool sh = (keyData & Keys.Shift) != 0;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: vp.VolumeBy(5); return true;
                case Keys.Down: vp.VolumeBy(-5); return true;
                case Keys.Right: vp.SeekBy(sh ? 30 : 5); return true;
                case Keys.Left: vp.SeekBy(sh ? -30 : -5); return true;
                case Keys.Space: vp.TogglePlay(); return true;
                case Keys.M: vp.ToggleMute(); return true;
                case Keys.T: OpenVideoEditor(false); return true;
                case Keys.C: OpenVideoEditor(true); return true;
                case Keys.Enter: if (vp.Failed) { OpenExternal(); return true; } break;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    [STAThread]
    static void Main(string[] a)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs ex) { MessageBox.Show(ex.Exception.Message, "Lite Media Viewer"); };

        if (!LicenseManager.IsActivated())
        {
            using (ActivationDialog actDlg = new ActivationDialog())
            {
                if (actDlg.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }
        }

        Application.Run(new Viewer(a.Length > 0 ? a[0] : null));
    }
}

// ====================================================================== ICONS
static class Icons
{
    public static readonly Color Fg = Color.FromArgb(218, 222, 230);
    static Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

    public static Bitmap Get(string n)
    {
        Bitmap b;
        if (!cache.TryGetValue(n, out b)) { b = Make(n, 20, Fg); cache[n] = b; }
        return b;
    }

    static PointF[] P(params float[] v)
    {
        PointF[] r = new PointF[v.Length / 2];
        for (int i = 0; i < r.Length; i++) r[i] = new PointF(v[2 * i], v[2 * i + 1]);
        return r;
    }

    static void ArcArrow(Graphics g, Pen p, Brush b, float cx, float cy, float r, float start, float sweep)
    {
        g.DrawArc(p, cx - r, cy - r, 2 * r, 2 * r, start, sweep);
        double a = (start + sweep) * Math.PI / 180.0, dir = sweep > 0 ? 1 : -1;
        float px = cx + r * (float)Math.Cos(a), py = cy + r * (float)Math.Sin(a);
        float tx = (float)(-Math.Sin(a) * dir), ty = (float)(Math.Cos(a) * dir), nx = (float)Math.Cos(a), ny = (float)Math.Sin(a);
        g.FillPolygon(b, new PointF[] { new PointF(px + tx * 4.2f, py + ty * 4.2f), new PointF(px + nx * 3.4f, py + ny * 3.4f), new PointF(px - nx * 3.4f, py - ny * 3.4f) });
    }

    public static Bitmap Make(string n, int size, Color fg)
    {
        if (n == "rotl") { Bitmap r = Make("rotr", size, fg); r.RotateFlip(RotateFlipType.RotateNoneFlipX); return r; }
        if (n == "flipv") { Bitmap r = Make("fliph", size, fg); r.RotateFlip(RotateFlipType.Rotate90FlipNone); return r; }
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        using (StringFormat sf = new StringFormat())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(size / 20f, size / 20f);
            sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
            Color c = fg;
            if (fg == Fg) { if (n == "del" || n == "no") c = Color.FromArgb(240, 112, 112); else if (n == "ok") c = Color.FromArgb(110, 214, 130); }
            using (Pen p = new Pen(c, 1.7f))
            using (SolidBrush br = new SolidBrush(c))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                PointF[] folder = P(2.5f, 15.5f, 2.5f, 4.5f, 8f, 4.5f, 10f, 6.5f, 17.5f, 6.5f, 17.5f, 15.5f, 2.5f, 15.5f);
                switch (n)
                {
                    case "open": g.DrawLines(p, folder); break;
                    case "folder": g.DrawLines(p, folder); g.FillEllipse(br, 8.3f, 9.3f, 3.4f, 3.4f); break;
                    case "prev": p.Width = 2.3f; g.DrawLines(p, P(12.5f, 4f, 6.5f, 10f, 12.5f, 16f)); break;
                    case "next": p.Width = 2.3f; g.DrawLines(p, P(7.5f, 4f, 13.5f, 10f, 7.5f, 16f)); break;
                    case "zout": case "zin":
                        g.DrawEllipse(p, 3f, 3f, 10f, 10f); g.DrawLine(p, 11.5f, 11.5f, 17f, 17f); g.DrawLine(p, 5.5f, 8f, 10.5f, 8f);
                        if (n == "zin") g.DrawLine(p, 8f, 5.5f, 8f, 10.5f);
                        break;
                    case "fit":
                        g.DrawLines(p, P(3f, 7f, 3f, 3f, 7f, 3f)); g.DrawLines(p, P(13f, 3f, 17f, 3f, 17f, 7f));
                        g.DrawLines(p, P(17f, 13f, 17f, 17f, 13f, 17f)); g.DrawLines(p, P(7f, 17f, 3f, 17f, 3f, 13f));
                        break;
                    case "actual":
                        using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Bold)) g.DrawString("1:1", f, br, new RectangleF(0, 0, 20, 21), sf);
                        break;
                    case "rotr": ArcArrow(g, p, br, 10f, 10f, 6.2f, -50f, 270f); break;
                    case "fliph":
                        g.DrawPolygon(p, P(8f, 5f, 8f, 16f, 2.5f, 16f)); g.FillPolygon(br, P(12f, 5f, 12f, 16f, 17.5f, 16f));
                        using (Pen d = new Pen(c, 1.2f)) { d.DashStyle = DashStyle.Dash; g.DrawLine(d, 10f, 2.5f, 10f, 18f); }
                        break;
                    case "crop": g.DrawLines(p, P(6f, 2f, 6f, 14f, 18f, 14f)); g.DrawLines(p, P(2f, 6f, 14f, 6f, 14f, 18f)); break;
                    case "resize":
                        g.DrawRectangle(p, 2.5f, 9.5f, 8f, 8f); g.DrawLine(p, 8f, 12f, 15.5f, 4.5f);
                        g.FillPolygon(br, P(17.5f, 2.5f, 11.5f, 3.5f, 16.5f, 8.5f));
                        break;
                    case "undo": ArcArrow(g, p, br, 10f, 10f, 6f, -15f, -215f); break;
                    case "revert": ArcArrow(g, p, br, 10f, 9f, 5.5f, -15f, -215f); g.DrawLine(p, 4f, 18f, 16f, 18f); break;
                    case "update": ArcArrow(g, p, br, 10f, 10f, 6f, -45f, 150f); ArcArrow(g, p, br, 10f, 10f, 6f, 135f, 150f); break;
                    case "copy":
                        g.DrawRectangle(p, 7.5f, 2.5f, 9f, 11f);
                        using (SolidBrush bg = new SolidBrush(DarkRenderer.Bg)) g.FillRectangle(bg, 3.5f, 6.5f, 9f, 11f);
                        g.DrawRectangle(p, 3.5f, 6.5f, 9f, 11f);
                        break;
                    case "save":
                        g.DrawRectangle(p, 2.5f, 2.5f, 15f, 15f); g.DrawRectangle(p, 6f, 2.5f, 7f, 5f); g.DrawRectangle(p, 5.5f, 11f, 9f, 6.5f);
                        break;
                    case "del":
                        g.DrawLine(p, 3f, 5.5f, 17f, 5.5f); g.DrawLines(p, P(8f, 5.5f, 8f, 3f, 12f, 3f, 12f, 5.5f));
                        g.DrawLines(p, P(5f, 5.5f, 6f, 17.5f, 14f, 17.5f, 15f, 5.5f));
                        g.DrawLine(p, 8.5f, 8.5f, 8.5f, 14.5f); g.DrawLine(p, 11.5f, 8.5f, 11.5f, 14.5f);
                        break;
                    case "slide": g.FillPolygon(br, P(6f, 3.5f, 16.5f, 10f, 6f, 16.5f)); break;
                    case "play": g.FillPolygon(br, P(6f, 3.5f, 16.5f, 10f, 6f, 16.5f)); break;
                    case "pause": g.FillRectangle(br, 5f, 3.5f, 3.6f, 13f); g.FillRectangle(br, 11.4f, 3.5f, 3.6f, 13f); break;
                    case "vol":
                        g.FillPolygon(br, P(2f, 7.5f, 6f, 7.5f, 10.5f, 3.5f, 10.5f, 16.5f, 6f, 12.5f, 2f, 12.5f));
                        g.DrawArc(p, 8.5f, 6.2f, 5f, 7.6f, -60f, 120f); g.DrawArc(p, 8.5f, 3.2f, 9f, 13.6f, -60f, 120f);
                        break;
                    case "mute":
                        g.FillPolygon(br, P(2f, 7.5f, 6f, 7.5f, 10.5f, 3.5f, 10.5f, 16.5f, 6f, 12.5f, 2f, 12.5f));
                        g.DrawLine(p, 13f, 7.5f, 18f, 12.5f); g.DrawLine(p, 18f, 7.5f, 13f, 12.5f);
                        break;
                    case "voldn":
                        g.FillPolygon(br, P(1.5f, 7.5f, 5f, 7.5f, 9f, 4f, 9f, 16f, 5f, 12.5f, 1.5f, 12.5f)); g.DrawLine(p, 12.5f, 10f, 18.5f, 10f);
                        break;
                    case "volup":
                        g.FillPolygon(br, P(1.5f, 7.5f, 5f, 7.5f, 9f, 4f, 9f, 16f, 5f, 12.5f, 1.5f, 12.5f)); g.DrawLine(p, 12.5f, 10f, 18.5f, 10f); g.DrawLine(p, 15.5f, 7f, 15.5f, 13f);
                        break;
                    case "ext": g.DrawRectangle(p, 2.5f, 5.5f, 11f, 11f); g.DrawLine(p, 9f, 11f, 17f, 3f); g.DrawLines(p, P(11f, 3f, 17f, 3f, 17f, 9f)); break;
                    case "full": g.DrawRectangle(p, 2.5f, 3.5f, 15f, 10f); g.DrawLine(p, 7f, 17f, 13f, 17f); g.DrawLine(p, 10f, 13.5f, 10f, 17f); break;
                    case "info": g.DrawEllipse(p, 2.5f, 2.5f, 15f, 15f); g.FillEllipse(br, 9f, 5.3f, 2.2f, 2.2f); g.DrawLine(p, 10f, 9f, 10f, 14f); break;
                    case "help":
                        g.DrawEllipse(p, 2.5f, 2.5f, 15f, 15f);
                        using (Font f = new Font("Segoe UI", 9.5f, FontStyle.Bold)) g.DrawString("?", f, br, new RectangleF(0, 0.5f, 20, 20), sf);
                        break;
                    case "trim":
                        g.DrawEllipse(p, 3f, 12f, 5f, 5f); g.DrawEllipse(p, 12f, 12f, 5f, 5f);
                        g.DrawLine(p, 6f, 12f, 14f, 3f); g.DrawLine(p, 14f, 12f, 6f, 3f);
                        break;
                    case "vcrop":
                        g.DrawLines(p, P(5f, 2f, 5f, 15f, 18f, 15f)); g.DrawLines(p, P(2f, 5f, 15f, 5f, 15f, 18f));
                        g.FillPolygon(br, P(8f, 7.5f, 12.5f, 10f, 8f, 12.5f));
                        break;
                    case "ok": p.Width = 2.4f; g.DrawLines(p, P(4f, 10.5f, 8.5f, 15f, 16.5f, 5f)); break;
                    case "no": p.Width = 2.2f; g.DrawLine(p, 5f, 5f, 15f, 15f); g.DrawLine(p, 15f, 5f, 5f, 15f); break;
                }
            }
        }
        return bmp;
    }
}

// ====================================================================== DARK RENDERER
class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return DarkRenderer.Bg; } }
    public override Color ImageMarginGradientBegin { get { return DarkRenderer.Bg; } }
    public override Color ImageMarginGradientMiddle { get { return DarkRenderer.Bg; } }
    public override Color ImageMarginGradientEnd { get { return DarkRenderer.Bg; } }
    public override Color MenuBorder { get { return DarkRenderer.Border; } }
    public override Color MenuItemBorder { get { return DarkRenderer.Hover; } }
    public override Color MenuItemSelected { get { return DarkRenderer.Hover; } }
    public override Color CheckBackground { get { return DarkRenderer.Accent; } }
    public override Color CheckSelectedBackground { get { return DarkRenderer.Accent; } }
    public override Color CheckPressedBackground { get { return DarkRenderer.Accent; } }
    public override Color SeparatorDark { get { return DarkRenderer.Border; } }
    public override Color SeparatorLight { get { return DarkRenderer.Border; } }
    public override Color ToolStripBorder { get { return DarkRenderer.Border; } }
}

class DarkRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color Bg = Color.FromArgb(34, 36, 40), StatusBg = Color.FromArgb(28, 29, 32), Border = Color.FromArgb(58, 61, 68),
        Hover = Color.FromArgb(62, 66, 74), Press = Color.FromArgb(78, 83, 93), Accent = Color.FromArgb(0, 105, 190), Text = Color.FromArgb(225, 228, 235);

    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    static GraphicsPath RR(Rectangle r, int d)
    {
        GraphicsPath gp = new GraphicsPath();
        gp.AddArc(r.X, r.Y, d, d, 180, 90); gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        gp.CloseFigure(); return gp;
    }
    static void Fill(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath gp = RR(r, 8)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, gp);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using (SolidBrush b = new SolidBrush(e.ToolStrip is StatusStrip ? StatusBg : Bg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using (Pen p = new Pen(Border))
        {
            if (e.ToolStrip is ToolStripDropDown) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            else if (e.ToolStrip is StatusStrip) e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
            else e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }
    }
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        ToolStripButton b = e.Item as ToolStripButton;
        bool chk = b != null && b.Checked;
        if (!e.Item.Enabled || !(e.Item.Selected || chk || e.Item.Pressed)) return;
        Color c = e.Item.Pressed ? Press : (chk ? (e.Item.Selected ? Color.FromArgb(20, 125, 215) : Accent) : Hover);
        Fill(e.Graphics, new Rectangle(1, 1, e.Item.Width - 3, e.Item.Height - 3), c);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled) Fill(e.Graphics, new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2), Hover);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using (Pen p = new Pen(Border))
        {
            if (e.Vertical) { int x = e.Item.Width / 2; e.Graphics.DrawLine(p, x, 8, x, e.Item.Height - 8); }
            else { int y = e.Item.Height / 2; e.Graphics.DrawLine(p, 30, y, e.Item.Width - 6, y); }
        }
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Color.FromArgb(105, 108, 116);
        base.OnRenderItemText(e);
    }
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image == null) return;
        if (e.Item.Enabled) { e.Graphics.DrawImage(e.Image, e.ImageRectangle); return; }
        ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = 0.32f;
        using (ImageAttributes ia = new ImageAttributes())
        {
            ia.SetColorMatrix(cm);
            e.Graphics.DrawImage(e.Image, e.ImageRectangle, 0, 0, e.Image.Width, e.Image.Height, GraphicsUnit.Pixel, ia);
        }
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Text; base.OnRenderArrow(e); }
    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        using (SolidBrush b = new SolidBrush(e.Item.Selected ? Hover : Bg)) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
    }
}

// ====================================================================== VIDEO / AUDIO PLAYBACK
class WmpHost : AxHost
{
    public WmpHost() : base("6BF52A52-394A-11d3-B153-00C04F79FAA6") { }
    public object Ocx() { return GetOcx(); }
}

class VideoPane : Panel
{
    public MethodInvoker FullHandler, EndedHandler;
    public bool Failed;

    const string RegKey = @"HKEY_CURRENT_USER\Software\LiteMediaViewer";
    WmpHost host; object ocx; string pending;
    int tries, volume = 80; bool endedFired, seeking, playing, muted, everPlayed; DateTime startedAt;
    Panel bar = new Panel();
    Button bPlay = new Button(), bMute = new Button(), bDn = new Button(), bUp = new Button(), bFull = new Button();
    TrackBar seek = new TrackBar(), vol = new TrackBar();
    Label lTime = new Label(), lVol = new Label(), lMsg = new Label();
    ToolTip tt = new ToolTip();
    System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();

    public VideoPane()
    {
        BackColor = Color.Black; Visible = false;
        bar.BackColor = DarkRenderer.Bg; bar.Height = 46;
        MakeBtn(bPlay, "pause", "Play / Pause   (Space)", delegate { TogglePlay(); });
        MakeBtn(bMute, "vol", "Mute   (M)", delegate { ToggleMute(); });
        MakeBtn(bDn, "voldn", "Volume down   (Down arrow / mouse wheel)", delegate { VolumeBy(-5); });
        MakeBtn(bUp, "volup", "Volume up   (Up arrow / mouse wheel)", delegate { VolumeBy(5); });
        MakeBtn(bFull, "full", "Fullscreen   (F)", delegate { if (FullHandler != null) FullHandler(); });
        foreach (Label l in new Label[] { lTime, lVol })
        {
            l.ForeColor = DarkRenderer.Text; l.BackColor = DarkRenderer.Bg; l.AutoSize = false; l.TextAlign = ContentAlignment.MiddleLeft;
        }
        lVol.TextAlign = ContentAlignment.MiddleRight;
        seek.AutoSize = false; seek.TickStyle = TickStyle.None; seek.Minimum = 0; seek.Maximum = 1000; seek.BackColor = DarkRenderer.Bg; seek.TabStop = false;
        vol.AutoSize = false; vol.TickStyle = TickStyle.None; vol.Minimum = 0; vol.Maximum = 100; vol.BackColor = DarkRenderer.Bg; vol.TabStop = false;
        seek.MouseDown += delegate(object s, MouseEventArgs e)
        {
            seeking = true;
            int v = (int)(1000.0 * (e.X - 12) / Math.Max(1, seek.Width - 24));
            seek.Value = v < 0 ? 0 : (v > 1000 ? 1000 : v);
        };
        seek.MouseUp += delegate { SeekFrac(seek.Value / 1000.0); seeking = false; GiveFocusBack(); };
        vol.ValueChanged += delegate { SetVolume(vol.Value, true); };
        vol.MouseUp += delegate { GiveFocusBack(); };
        bar.Controls.Add(seek); bar.Controls.Add(vol); bar.Controls.Add(lTime); bar.Controls.Add(lVol);
        lMsg.ForeColor = Color.Gainsboro; lMsg.BackColor = Color.Black; lMsg.TextAlign = ContentAlignment.MiddleCenter; lMsg.AutoSize = false; lMsg.Visible = false;
        Controls.Add(lMsg); Controls.Add(bar);
        tick.Interval = 200; tick.Tick += delegate { Poll(); };
        volume = LoadVol(); SetVolume(volume, false);
        LayoutAll();
    }

    void MakeBtn(Button b, string icon, string tip, MethodInvoker act)
    {
        b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = DarkRenderer.Hover; b.FlatAppearance.MouseDownBackColor = DarkRenderer.Press;
        b.BackColor = DarkRenderer.Bg; b.Image = Icons.Get(icon); b.ImageAlign = ContentAlignment.MiddleCenter;
        b.TabStop = false; b.Cursor = Cursors.Hand;
        tt.SetToolTip(b, tip);
        b.Click += delegate { act(); GiveFocusBack(); };
        bar.Controls.Add(b);
    }
    void GiveFocusBack() { if (Parent != null) Parent.Focus(); }

    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutAll(); }
    void LayoutAll()
    {
        int bh = 46, vh = Math.Max(1, Height - bh);
        bar.SetBounds(0, Math.Max(0, Height - bh), Width, bh);
        if (host != null) host.SetBounds(0, 0, Width, vh);
        lMsg.SetBounds(0, 0, Width, vh);
        int w = Math.Max(300, bar.ClientSize.Width), x = 6;
        bPlay.SetBounds(x, 7, 36, 32); x += 40;
        lTime.SetBounds(x, 13, 104, 20); x += 108;
        int r = w - 6;
        bFull.SetBounds(r - 36, 7, 36, 32); r -= 42;
        lVol.SetBounds(r - 48, 13, 48, 20); r -= 52;
        bUp.SetBounds(r - 36, 7, 36, 32); r -= 38;
        vol.SetBounds(r - 96, 8, 96, 30); r -= 98;
        bDn.SetBounds(r - 36, 7, 36, 32); r -= 38;
        bMute.SetBounds(r - 36, 7, 36, 32); r -= 44;
        seek.SetBounds(x, 8, Math.Max(40, r - x), 30);
    }

    static object Get(object o, string n) { return o.GetType().InvokeMember(n, BindingFlags.GetProperty, null, o, new object[0]); }
    static void Put(object o, string n, object v) { o.GetType().InvokeMember(n, BindingFlags.SetProperty, null, o, new object[] { v }); }
    static object Call(object o, string n) { return o.GetType().InvokeMember(n, BindingFlags.InvokeMethod, null, o, new object[0]); }

    public void Open(string path)
    {
        pending = path; Failed = false; tries = 0; endedFired = false; everPlayed = false; startedAt = DateTime.Now; lMsg.Visible = false;
        if (!Visible) Visible = true;
        if (host == null && !CreateHost()) return;
        LayoutAll(); tick.Start(); TryStart();
    }

    bool CreateHost()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\{6BF52A52-394A-11d3-B153-00C04F79FAA6}"))
            {
                if (k == null) { Fail("Windows Media Player is not installed on this PC."); return false; }
            }
            host = new WmpHost();
            Controls.Add(host);
            host.SetBounds(0, 0, Width, Math.Max(1, Height - bar.Height));
            host.SendToBack();
            return true;
        }
        catch (Exception e) { host = null; Fail(e.Message); return false; }
    }

    void TryStart()
    {
        if (pending == null || host == null) return;
        if (ocx == null)
        {
            if (!host.IsHandleCreated) return;
            try { ocx = host.Ocx(); } catch (Exception) { ocx = null; }
            if (ocx == null) return;
            try { Put(ocx, "uiMode", "none"); } catch (Exception) { }
            try { Put(ocx, "enableContextMenu", false); } catch (Exception) { }
            try { host.Enabled = false; } catch (Exception) { }
        }
        try
        {
            string p = pending; pending = null;
            Put(Get(ocx, "settings"), "autoStart", true);
            ApplyAudio();
            Put(ocx, "URL", p);
        }
        catch (Exception e) { Fail(e.Message); }
    }

    public void Close()
    {
        tick.Stop(); pending = null;
        try { if (ocx != null) { Call(Get(ocx, "controls"), "stop"); Put(ocx, "URL", ""); } } catch (Exception) { }
        lMsg.Visible = false; Failed = false; Visible = false;
    }

    void Fail(string why)
    {
        Failed = true; tick.Stop(); pending = null;
        lMsg.Text = "Windows Media Player could not play this file.\r\n(" + why + ")\r\n\r\nPress Enter to open it with your default player.";
        lMsg.Visible = true; lMsg.BringToFront(); bar.BringToFront();
    }

    double Duration()
    {
        try { object cm = Get(ocx, "currentMedia"); if (cm == null) return 0; return Convert.ToDouble(Get(cm, "duration")); }
        catch (Exception) { return 0; }
    }
    static string Tm(double s)
    {
        if (s < 0 || double.IsNaN(s)) s = 0;
        int t = (int)s, h = t / 3600, m = (t / 60) % 60, sec = t % 60;
        return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, sec) : string.Format("{0}:{1:00}", m, sec);
    }
    void Poll()
    {
        if (pending != null) { if (++tries > 40) Fail("Windows Media Player could not start."); else TryStart(); return; }
        if (ocx == null || Failed) return;
        try
        {
            int ps = Convert.ToInt32(Get(ocx, "playState"));
            object ctl = Get(ocx, "controls");
            double pos = Convert.ToDouble(Get(ctl, "currentPosition")), dur = Duration();
            playing = (ps == 3);
            if (playing || dur > 0) everPlayed = true;
            bPlay.Image = Icons.Get(playing ? "pause" : "play");
            lTime.Text = Tm(pos) + " / " + Tm(dur);
            if (!seeking)
            {
                int v = dur > 0 ? (int)Math.Max(0, Math.Min(1000, pos / dur * 1000)) : 0;
                if (seek.Value != v) seek.Value = v;
            }
            if (ps == 8 && !endedFired) { endedFired = true; if (EndedHandler != null) EndedHandler(); }
            if (!everPlayed && (DateTime.Now - startedAt).TotalSeconds > 5) Fail("format or codec is not supported");
        }
        catch (Exception) { }
    }

    public void TogglePlay()
    {
        if (ocx == null || Failed) return;
        try { object c = Get(ocx, "controls"); if (playing) Call(c, "pause"); else Call(c, "play"); } catch (Exception) { }
    }
    public void SeekBy(double s)
    {
        if (ocx == null || Failed) return;
        try
        {
            object c = Get(ocx, "controls");
            double pos = Convert.ToDouble(Get(c, "currentPosition")), dur = Duration(), n = pos + s;
            if (n < 0) n = 0;
            if (dur > 0 && n > dur - 0.5) n = Math.Max(0, dur - 0.5);
            Put(c, "currentPosition", n);
        }
        catch (Exception) { }
    }
    void SeekFrac(double f)
    {
        if (ocx == null || Failed) return;
        try { double dur = Duration(); if (dur > 0) Put(Get(ocx, "controls"), "currentPosition", f * dur); } catch (Exception) { }
    }

    public void VolumeBy(int d) { SetVolume(volume + d, true); }
    public void ToggleMute() { muted = !muted; UpdateVolUi(); ApplyAudio(); }
    void SetVolume(int v, bool save)
    {
        if (v < 0) v = 0;
        if (v > 100) v = 100;
        volume = v; if (v > 0 && muted) muted = false;
        if (vol.Value != v) vol.Value = v;
        UpdateVolUi(); ApplyAudio();
        if (save) SaveVol(v);
    }
    void UpdateVolUi()
    {
        bool off = muted || volume == 0;
        lVol.Text = off ? "Muted" : volume + "%";
        bMute.Image = Icons.Get(off ? "mute" : "vol");
    }
    void ApplyAudio()
    {
        if (ocx == null) return;
        try { object s = Get(ocx, "settings"); Put(s, "volume", volume); Put(s, "mute", muted); } catch (Exception) { }
    }

    public double Position() { try { return Convert.ToDouble(Get(Get(ocx, "controls"), "currentPosition")); } catch (Exception) { return 0; } }
    public void Pause() { if (ocx == null || Failed) return; try { if (playing) Call(Get(ocx, "controls"), "pause"); } catch (Exception) { } }
    public string InfoText()
    {
        return "  Duration     : " + Tm(Duration()) + "\r\n  Volume       : " + (muted ? "muted" : volume + "%") + "\r\n  Engine       : Windows Media Player";
    }

    static int LoadVol()
    {
        try { object o = Microsoft.Win32.Registry.GetValue(RegKey, "Volume", null); if (o != null) { int v = Convert.ToInt32(o); return v < 0 ? 0 : (v > 100 ? 100 : v); } }
        catch (Exception) { }
        return 80;
    }
    static void SaveVol(int v)
    {
        try { Microsoft.Win32.Registry.SetValue(RegKey, "Volume", v, Microsoft.Win32.RegistryValueKind.DWord); } catch (Exception) { }
    }
}

// ====================================================================== VIDEO TRIM / CROP  (uses ffmpeg.exe, kept next to the program)
static class Ffmpeg
{
    public static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
    public static string Sec(double s) { return s.ToString("0.###", CultureInfo.InvariantCulture); }

    public static string Tc(double s)
    {
        if (s < 0 || double.IsNaN(s)) s = 0;
        long t = (long)Math.Floor(s); int ms = (int)Math.Round((s - Math.Floor(s)) * 1000);
        if (ms >= 1000) { t++; ms -= 1000; }
        return string.Format("{0:00}:{1:00}:{2:00}.{3:000}", t / 3600, (t / 60) % 60, t % 60, ms);
    }
    public static bool TryTime(string s, out double v)
    {
        v = 0; s = s.Trim().Replace(',', '.');
        if (s.Length == 0) return false;
        string[] p = s.Split(':');
        if (p.Length > 3) return false;
        double mult = 1, tot = 0;
        for (int i = p.Length - 1; i >= 0; i--)
        {
            double x;
            if (!double.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) || x < 0) return false;
            tot += x * mult; mult *= 60;
        }
        v = tot; return true;
    }

    public static string Find()
    {
        try
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            string x = Path.Combine(b, "ffmpeg.exe");
            if (File.Exists(x)) return x;
            x = Path.Combine(Path.Combine(b, "tools"), "ffmpeg.exe");
            if (File.Exists(x)) return x;
            string pa = Environment.GetEnvironmentVariable("PATH");
            if (pa != null)
                foreach (string d in pa.Split(';'))
                {
                    try { string y = Path.Combine(d.Trim().Trim('"'), "ffmpeg.exe"); if (File.Exists(y)) return y; } catch (Exception) { }
                }
        }
        catch (Exception) { }
        return null;
    }

    // runs ffmpeg and returns everything it printed (stdout + stderr)
    public static string RunCapture(string exe, string args, int timeoutMs, out int exitCode)
    {
        exitCode = -1;
        ProcessStartInfo psi = new ProcessStartInfo(exe, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        using (Process p = Process.Start(psi))
        {
            StringBuilder so = new StringBuilder();
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (so) so.AppendLine(e.Data); };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (so) so.AppendLine(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } }
            else p.WaitForExit();
            exitCode = p.HasExited ? p.ExitCode : -1;
            lock (so) return so.ToString();
        }
    }

    public static void Probe(string ff, string file, out double dur, out bool hasVideo, out bool hasAudio)
    {
        dur = 0; hasVideo = false; hasAudio = false;
        int code;
        string o = RunCapture(ff, "-hide_banner -i " + Q(file), 20000, out code);
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(o, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (m.Success)
            dur = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        foreach (string line in o.Split('\n'))
        {
            if (line.Contains(": Video:") && !line.Contains("attached pic")) hasVideo = true;
            if (line.Contains(": Audio:")) hasAudio = true;
        }
    }

    // one picture of the video at time t (already turned the right way up)
    public static Bitmap Frame(string ff, string file, double t)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "lmv_frame_" + Process.GetCurrentProcess().Id + "_" + Environment.TickCount + ".jpg");
        try
        {
            int code;
            RunCapture(ff, "-hide_banner -loglevel error -y -ss " + Sec(t) + " -i " + Q(file) + " -frames:v 1 -q:v 2 " + Q(tmp), 30000, out code);
            if (!File.Exists(tmp)) return null;
            using (FileStream fs = File.OpenRead(tmp))
            using (Image im = Image.FromStream(fs)) return new Bitmap(im);
        }
        catch (Exception) { return null; }
        finally { try { File.Delete(tmp); } catch (Exception) { } }
    }

    public static string EditArgs(string input, string output, double start, double len, bool hasVideo, bool crop, int cx, int cy, int cw, int ch, bool precise, bool mute, int crf)
    {
        StringBuilder a = new StringBuilder();
        a.Append("-hide_banner -loglevel error -nostdin -y -progress pipe:1 -nostats");
        a.Append(" -ss ").Append(Sec(start)).Append(" -i ").Append(Q(input)).Append(" -t ").Append(Sec(len));
        a.Append(hasVideo ? " -map 0:v:0 -map 0:a:0?" : " -map 0:a:0");
        if (!precise || !hasVideo) a.Append(" -c copy -avoid_negative_ts make_zero");
        else
        {
            if (crop) a.Append(" -vf crop=" + cw + ":" + ch + ":" + cx + ":" + cy);
            a.Append(" -c:v libx264 -preset veryfast -crf " + crf + " -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart");
        }
        if (mute && hasVideo) a.Append(" -an");
        a.Append(" ").Append(Q(output));
        return a.ToString();
    }
}

// picture preview with a draggable crop box
class FramePanel : Control
{
    public Bitmap Frame; public bool CropOn; public float Ratio; public RectangleF Sel; public string Msg = "";
    public event EventHandler SelChanged;
    int drag, hnd; RectangleF s0; PointF st;

    public FramePanel() { DoubleBuffered = true; BackColor = Color.FromArgb(18, 18, 20); SetStyle(ControlStyles.ResizeRedraw, true); }

    public void SetFrame(Bitmap b) { Bitmap old = Frame; Frame = b; if (old != null) old.Dispose(); Invalidate(); }

    float Sc() { return Frame == null ? 1f : Math.Max(0.01f, Math.Min((Width - 16f) / Frame.Width, (Height - 16f) / Frame.Height)); }
    PointF Org(float sc) { return new PointF((Width - Frame.Width * sc) / 2f, (Height - Frame.Height * sc) / 2f); }
    PointF ToImg(Point s) { float sc = Sc(); PointF o = Org(sc); return new PointF((s.X - o.X) / sc, (s.Y - o.Y) / sc); }
    RectangleF ToScr(RectangleF r) { float sc = Sc(); PointF o = Org(sc); return new RectangleF(o.X + r.X * sc, o.Y + r.Y * sc, r.Width * sc, r.Height * sc); }
    PointF Clamp(PointF p) { return new PointF(Math.Max(0, Math.Min(Frame.Width, p.X)), Math.Max(0, Math.Min(Frame.Height, p.Y))); }
    static RectangleF Norm(float x1, float y1, float x2, float y2) { return RectangleF.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)); }
    void Fire() { if (SelChanged != null) SelChanged(this, EventArgs.Empty); }

    public void DefaultSel()
    {
        if (Frame == null) return;
        Sel = new RectangleF(Frame.Width * 0.1f, Frame.Height * 0.1f, Frame.Width * 0.8f, Frame.Height * 0.8f);
        ApplyRatio();
    }
    public void FullSel() { if (Frame == null) return; Sel = new RectangleF(0, 0, Frame.Width, Frame.Height); Fire(); Invalidate(); }
    public void ApplyRatio()
    {
        if (Frame != null && Ratio > 0)
        {
            float cx = Sel.X + Sel.Width / 2f, cy = Sel.Y + Sel.Height / 2f, w = Sel.Width, h = w / Ratio;
            if (h > Sel.Height) { h = Sel.Height; w = h * Ratio; }
            Sel = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
        }
        Fire(); Invalidate();
    }

    PointF[] Pts(RectangleF r)
    {
        float mx = r.X + r.Width / 2f, my = r.Y + r.Height / 2f;
        return new PointF[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom),
                              new PointF(mx, r.Top), new PointF(r.Right, my), new PointF(mx, r.Bottom), new PointF(r.Left, my) };
    }
    int Hit(Point s)
    {
        RectangleF rs = ToScr(Sel); PointF[] h = Pts(rs);
        for (int i = 0; i < 8; i++) if (Math.Abs(s.X - h[i].X) <= 7 && Math.Abs(s.Y - h[i].Y) <= 7) return i;
        return rs.Contains(s.X, s.Y) ? 8 : -1;
    }
    RectangleF Constrain(PointF a, PointF p)
    {
        float w = p.X - a.X, h = p.Y - a.Y;
        if (Ratio > 0)
        {
            float aw = Math.Abs(w), ah = Math.Abs(h), sx = w < 0 ? -1 : 1, sy = h < 0 ? -1 : 1;
            if (aw / Ratio > ah) aw = ah * Ratio; else ah = aw / Ratio;
            float maxW = sx > 0 ? Frame.Width - a.X : a.X, maxH = sy > 0 ? Frame.Height - a.Y : a.Y;
            if (aw > maxW) { aw = maxW; ah = aw / Ratio; }
            if (ah > maxH) { ah = maxH; aw = ah * Ratio; }
            w = sx * aw; h = sy * ah;
        }
        return Norm(a.X, a.Y, a.X + w, a.Y + h);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!CropOn || Frame == null || e.Button != MouseButtons.Left) return;
        PointF p = Clamp(ToImg(e.Location)); int h = Hit(e.Location);
        if (h >= 0 && h < 8) { drag = 3; hnd = h; s0 = Sel; }
        else if (h == 8) { drag = 2; s0 = Sel; st = p; }
        else { drag = 1; st = p; Sel = new RectangleF(p.X, p.Y, 0, 0); }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!CropOn || Frame == null) return;
        if (drag == 0)
        {
            int h = Hit(e.Location);
            Cursor = (h == 0 || h == 2) ? Cursors.SizeNWSE : (h == 1 || h == 3) ? Cursors.SizeNESW : (h == 4 || h == 6) ? Cursors.SizeNS : (h == 5 || h == 7) ? Cursors.SizeWE : h == 8 ? Cursors.SizeAll : Cursors.Cross;
            return;
        }
        PointF p = Clamp(ToImg(e.Location));
        if (drag == 1) Sel = Constrain(st, p);
        else if (drag == 2)
        {
            float nx = Math.Max(0, Math.Min(Frame.Width - s0.Width, s0.X + p.X - st.X));
            float ny = Math.Max(0, Math.Min(Frame.Height - s0.Height, s0.Y + p.Y - st.Y));
            Sel = new RectangleF(nx, ny, s0.Width, s0.Height);
        }
        else
        {
            switch (hnd)
            {
                case 0: Sel = Constrain(new PointF(s0.Right, s0.Bottom), p); break;
                case 1: Sel = Constrain(new PointF(s0.Left, s0.Bottom), p); break;
                case 2: Sel = Constrain(new PointF(s0.Left, s0.Top), p); break;
                case 3: Sel = Constrain(new PointF(s0.Right, s0.Top), p); break;
                case 4: Sel = RectangleF.FromLTRB(s0.Left, Math.Min(p.Y, s0.Bottom - 2), s0.Right, s0.Bottom); break;
                case 5: Sel = RectangleF.FromLTRB(s0.Left, s0.Top, Math.Max(p.X, s0.Left + 2), s0.Bottom); break;
                case 6: Sel = RectangleF.FromLTRB(s0.Left, s0.Top, s0.Right, Math.Max(p.Y, s0.Top + 2)); break;
                default: Sel = RectangleF.FromLTRB(Math.Min(p.X, s0.Right - 2), s0.Top, s0.Right, s0.Bottom); break;
            }
        }
        Fire(); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (drag == 0) return;
        drag = 0;
        if (Sel.Width < 4 || Sel.Height < 4) DefaultSel(); else Fire();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.Clear(BackColor);
        if (Frame == null)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Msg, Font, Brushes.Gray, ClientRectangle, sf);
            }
            return;
        }
        float sc = Sc(); PointF o = Org(sc);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(Frame, o.X, o.Y, Frame.Width * sc, Frame.Height * sc);
        if (!CropOn) return;
        RectangleF rs = ToScr(Sel); Rectangle ri = Rectangle.Round(rs);
        g.ExcludeClip(ri);
        using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(dim, o.X, o.Y, Frame.Width * sc, Frame.Height * sc);
        g.ResetClip();
        using (Pen p1 = new Pen(Color.Black, 3), p2 = new Pen(Color.White, 1), p3 = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
        {
            g.DrawRectangle(p1, ri.X, ri.Y, ri.Width, ri.Height);
            g.DrawRectangle(p2, ri.X, ri.Y, ri.Width, ri.Height);
            for (int k = 1; k < 3; k++)
            {
                float gx = rs.X + rs.Width * k / 3f, gy = rs.Y + rs.Height * k / 3f;
                g.DrawLine(p3, gx, rs.Y, gx, rs.Bottom); g.DrawLine(p3, rs.X, gy, rs.Right, gy);
            }
        }
        foreach (PointF hp in Pts(rs)) { g.FillRectangle(Brushes.White, hp.X - 4, hp.Y - 4, 8, 8); g.DrawRectangle(Pens.Black, hp.X - 4, hp.Y - 4, 8, 8); }
        string t = ((int)Math.Round(Sel.Width) & ~1) + " x " + ((int)Math.Round(Sel.Height) & ~1);
        SizeF ts = g.MeasureString(t, Font);
        float ty = Math.Max(0, rs.Y - ts.Height - 2);
        g.FillRectangle(Brushes.Black, rs.X, ty, ts.Width, ts.Height);
        g.DrawString(t, Font, Brushes.White, rs.X, ty);
    }
}

class VideoEditDialog : Form
{
    public string SavedPath;
    string ff, input, jobOut; double dur, jobLen; bool hasVideo, hasAudio;
    FramePanel pic = new FramePanel();
    TrackBar scrub = new TrackBar();
    Label lblNow = new Label(), lblLen = new Label(), lblStatus = new Label(), lblCropInfo = new Label();
    TextBox txtStart = new TextBox(), txtEnd = new TextBox();
    CheckBox chkCrop = new CheckBox(), chkMute = new CheckBox();
    ComboBox cbRatio = new ComboBox(), cbQ = new ComboBox();
    NumericUpDown nX = new NumericUpDown(), nY = new NumericUpDown(), nW = new NumericUpDown(), nH = new NumericUpDown();
    RadioButton rbFast = new RadioButton(), rbPrecise = new RadioButton();
    Button btnSave = new Button(), btnClose = new Button(), btnCancel = new Button(), btnStart = new Button(), btnEnd = new Button(), btnBack = new Button(), btnFwd = new Button(), btnFull = new Button();
    ProgressBar prog = new ProgressBar();
    Panel pnlRight = new Panel(), pnlLeft = new Panel();
    System.Windows.Forms.Timer tFrame = new System.Windows.Forms.Timer();
    int seq, vw, vh; bool busy, cancelled, syncing, firstFrame = true;
    Process proc; StringBuilder errText = new StringBuilder();

    static decimal Cd(double v, decimal mn, decimal mx) { decimal d = (decimal)Math.Round(v); return d < mn ? mn : (d > mx ? mx : d); }
    static Label Lb(string t, int x, int y, int w) { Label l = new Label(); l.Text = t; l.SetBounds(x, y, w, 20); return l; }
    static Button Bt(string t, int x, int y, int w, int h, EventHandler click) { Button b = new Button(); b.Text = t; b.SetBounds(x, y, w, h); b.Click += click; return b; }
    static float ParseRatio(string t)
    {
        int k = t.IndexOf(':'); float a, b;
        if (k > 0 && float.TryParse(t.Substring(0, k), out a) && float.TryParse(t.Substring(k + 1), out b) && b > 0) return a / b;
        return 0;
    }

    public VideoEditDialog(string ffmpeg, string file, double duration, bool video, bool audio, double startPos, bool cropFirst)
    {
        ff = ffmpeg; input = file; dur = duration; hasVideo = video; hasAudio = audio;
        Text = "Edit video - trim and crop - " + Path.GetFileName(file);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(980, 640);
        Font = new Font("Segoe UI", 9f);

        pic.SetBounds(12, 12, 620, 372); pic.Msg = video ? "Loading picture..." : "Audio file - there is no picture to crop";
        pnlLeft.SetBounds(12, 388, 620, 74);
        scrub.SetBounds(0, 0, 620, 32); scrub.Minimum = 0; scrub.Maximum = 1000; scrub.TickStyle = TickStyle.None; scrub.SmallChange = 2; scrub.LargeChange = 50;
        lblNow.SetBounds(0, 38, 190, 22);
        btnStart = Bt("[  Set as start", 190, 34, 118, 28, delegate { txtStart.Text = Ffmpeg.Tc(ScrubT()); UpdateLen(); });
        btnEnd = Bt("Set as end  ]", 312, 34, 118, 28, delegate { txtEnd.Text = Ffmpeg.Tc(ScrubT()); UpdateLen(); });
        btnBack = Bt("- 1 s", 440, 34, 56, 28, delegate { SetScrub(ScrubT() - 1); });
        btnFwd = Bt("+ 1 s", 500, 34, 56, 28, delegate { SetScrub(ScrubT() + 1); });
        pnlLeft.Controls.AddRange(new Control[] { scrub, lblNow, btnStart, btnEnd, btnBack, btnFwd });

        prog.SetBounds(12, 556, 620, 20); prog.Minimum = 0; prog.Maximum = 100;
        lblStatus.SetBounds(12, 580, 620, 52); lblStatus.Text = "Move the slider to find a moment, then press \"Set as start\" / \"Set as end\".";

        pnlRight.SetBounds(644, 8, 330, 540);
        GroupBox gbT = new GroupBox(); gbT.Text = "1.  Trim  (keep only this part)"; gbT.SetBounds(0, 0, 330, 120);
        gbT.Controls.Add(Lb("Start", 12, 28, 40)); txtStart.SetBounds(60, 25, 130, 22); gbT.Controls.Add(txtStart);
        gbT.Controls.Add(Lb("End", 12, 58, 40)); txtEnd.SetBounds(60, 55, 130, 22); gbT.Controls.Add(txtEnd);
        lblLen.SetBounds(12, 88, 300, 22); gbT.Controls.Add(lblLen);

        GroupBox gbC = new GroupBox(); gbC.Text = "2.  Crop  (cut away the edges)"; gbC.SetBounds(0, 126, 330, 200);
        chkCrop.Text = "Crop the picture"; chkCrop.SetBounds(12, 22, 200, 22); gbC.Controls.Add(chkCrop);
        gbC.Controls.Add(Lb("Shape", 12, 52, 50)); cbRatio.DropDownStyle = ComboBoxStyle.DropDownList; cbRatio.SetBounds(70, 49, 110, 22);
        cbRatio.Items.AddRange(new object[] { "Free", "1:1", "4:3", "3:2", "16:9", "9:16" }); cbRatio.SelectedIndex = 0; gbC.Controls.Add(cbRatio);
        gbC.Controls.Add(Lb("X", 12, 84, 20)); nX.SetBounds(34, 81, 80, 22); gbC.Controls.Add(nX);
        gbC.Controls.Add(Lb("Y", 130, 84, 20)); nY.SetBounds(152, 81, 80, 22); gbC.Controls.Add(nY);
        gbC.Controls.Add(Lb("W", 12, 114, 20)); nW.SetBounds(34, 111, 80, 22); gbC.Controls.Add(nW);
        gbC.Controls.Add(Lb("H", 130, 114, 20)); nH.SetBounds(152, 111, 80, 22); gbC.Controls.Add(nH);
        btnFull = Bt("Whole picture", 12, 144, 110, 26, delegate { pic.FullSel(); }); gbC.Controls.Add(btnFull);
        lblCropInfo.SetBounds(130, 148, 190, 22); gbC.Controls.Add(lblCropInfo);
        Label lh = Lb("Drag the box on the picture, or type the numbers (pixels).", 12, 174, 310); gbC.Controls.Add(lh);

        GroupBox gbO = new GroupBox(); gbO.Text = "3.  Save"; gbO.SetBounds(0, 332, 330, 200);
        rbFast.Text = "Fast copy - no quality loss. Cuts at the nearest keyframe, same format. Not possible with crop."; rbFast.SetBounds(12, 22, 306, 52);
        rbPrecise.Text = "Precise - exact cut, saved as MP4 (H.264). Slower. Needed for crop."; rbPrecise.SetBounds(12, 78, 306, 40);
        chkMute.Text = "Remove the sound"; chkMute.SetBounds(12, 124, 200, 22);
        gbO.Controls.Add(Lb("Quality", 12, 156, 54)); cbQ.DropDownStyle = ComboBoxStyle.DropDownList; cbQ.SetBounds(70, 153, 200, 22);
        cbQ.Items.AddRange(new object[] { "High (big file)", "Good (recommended)", "Small file" }); cbQ.SelectedIndex = 1;
        gbO.Controls.AddRange(new Control[] { rbFast, rbPrecise, chkMute, cbQ });
        pnlRight.Controls.AddRange(new Control[] { gbT, gbC, gbO });

        btnSave = Bt("Save as...", 644, 560, 150, 36, delegate { DoSave(); });
        btnClose = Bt("Close", 806, 560, 100, 36, delegate { Close(); });
        btnCancel = Bt("Stop", 806, 560, 100, 36, delegate { cancelled = true; try { if (proc != null) proc.Kill(); } catch (Exception) { } });
        btnCancel.Visible = false;
        Controls.AddRange(new Control[] { pic, pnlLeft, prog, lblStatus, pnlRight, btnSave, btnClose, btnCancel });

        // initial values
        txtStart.Text = Ffmpeg.Tc(0); txtEnd.Text = Ffmpeg.Tc(dur); UpdateLen();
        rbPrecise.Checked = video; rbFast.Checked = !video;
        rbPrecise.Enabled = video; chkMute.Enabled = video && audio; chkCrop.Enabled = video; cbQ.Enabled = video;
        SetCropControls(false); UpdateModeUi();
        double sp = startPos; if (sp < 0 || sp > dur) sp = 0;
        SetScrub(sp);

        // events
        scrub.ValueChanged += delegate { OnScrub(); };
        tFrame.Interval = 250; tFrame.Tick += delegate { tFrame.Stop(); RequestFrame(ScrubT()); };
        txtStart.Leave += delegate { NormTime(txtStart); UpdateLen(); }; txtEnd.Leave += delegate { NormTime(txtEnd); UpdateLen(); };
        txtStart.TextChanged += delegate { UpdateLen(); }; txtEnd.TextChanged += delegate { UpdateLen(); };
        chkCrop.CheckedChanged += delegate
        {
            pic.CropOn = chkCrop.Checked; SetCropControls(chkCrop.Checked);
            if (chkCrop.Checked) rbPrecise.Checked = true;
            UpdateModeUi(); pic.Invalidate();
        };
        rbFast.CheckedChanged += delegate { UpdateModeUi(); };
        cbRatio.SelectedIndexChanged += delegate { pic.Ratio = ParseRatio(cbRatio.Text); pic.ApplyRatio(); };
        pic.SelChanged += delegate { SyncNumerics(); };
        EventHandler numChanged = delegate { NumChanged(); };
        nX.ValueChanged += numChanged; nY.ValueChanged += numChanged; nW.ValueChanged += numChanged; nH.ValueChanged += numChanged;
        FormClosing += OnClosing;
        FormClosed += delegate { tFrame.Stop(); pic.SetFrame(null); };
        if (cropFirst) chkCrop.Checked = true;
    }

    double ScrubT() { return dur * scrub.Value / 1000.0; }
    void SetScrub(double t)
    {
        if (t < 0) t = 0; if (t > dur) t = dur;
        int v = dur > 0 ? (int)Math.Round(t / dur * 1000) : 0;
        scrub.Value = Math.Max(0, Math.Min(1000, v));
        OnScrub();
    }
    void OnScrub() { lblNow.Text = Ffmpeg.Tc(ScrubT()) + " / " + Ffmpeg.Tc(dur); tFrame.Stop(); tFrame.Start(); }

    void NormTime(TextBox t) { double v; if (Ffmpeg.TryTime(t.Text, out v)) t.Text = Ffmpeg.Tc(Math.Min(v, dur)); }
    void UpdateLen()
    {
        double s, e;
        if (Ffmpeg.TryTime(txtStart.Text, out s) && Ffmpeg.TryTime(txtEnd.Text, out e) && e > s)
        { lblLen.ForeColor = SystemColors.ControlText; lblLen.Text = "Length of the result:  " + Ffmpeg.Tc(Math.Min(e, dur) - s); }
        else { lblLen.ForeColor = Color.Firebrick; lblLen.Text = "Check the times (end must be after start)"; }
    }

    void SetCropControls(bool on)
    {
        bool en = on && hasVideo;
        cbRatio.Enabled = en; nX.Enabled = en; nY.Enabled = en; nW.Enabled = en; nH.Enabled = en; btnFull.Enabled = en;
    }
    void UpdateModeUi()
    {
        bool crop = chkCrop.Checked && hasVideo;
        rbFast.Enabled = !crop;
        cbQ.Enabled = hasVideo && (rbPrecise.Checked || crop);
    }

    void RequestFrame(double t)
    {
        if (!hasVideo) return;
        int my = ++seq;
        ThreadPool.QueueUserWorkItem(delegate
        {
            Bitmap b = Ffmpeg.Frame(ff, input, t);
            if (b == null && t > 0) b = Ffmpeg.Frame(ff, input, 0);
            try { BeginInvoke(new MethodInvoker(delegate { GotFrame(my, b); })); }
            catch (Exception) { if (b != null) b.Dispose(); }
        });
    }
    void GotFrame(int my, Bitmap b)
    {
        if (my != seq || IsDisposed) { if (b != null) b.Dispose(); return; }
        if (b == null) { lblStatus.Text = "Could not read a picture at this moment."; return; }
        bool changed = firstFrame || b.Width != vw || b.Height != vh;
        vw = b.Width; vh = b.Height;
        pic.SetFrame(b);
        if (changed)
        {
            firstFrame = false; syncing = true;
            nW.Minimum = 2; nH.Minimum = 2; nW.Maximum = vw; nH.Maximum = vh; nX.Maximum = Math.Max(0, vw - 2); nY.Maximum = Math.Max(0, vh - 2);
            syncing = false;
            pic.Ratio = ParseRatio(cbRatio.Text); pic.DefaultSel();
        }
    }

    void SyncNumerics()
    {
        syncing = true;
        RectangleF s = pic.Sel;
        nX.Value = Cd(s.X, nX.Minimum, nX.Maximum); nY.Value = Cd(s.Y, nY.Minimum, nY.Maximum);
        nW.Value = Cd(s.Width, nW.Minimum, nW.Maximum); nH.Value = Cd(s.Height, nH.Minimum, nH.Maximum);
        syncing = false;
        lblCropInfo.Text = "Result:  " + ((int)nW.Value & ~1) + " x " + ((int)nH.Value & ~1);
    }
    void NumChanged()
    {
        if (syncing || pic.Frame == null) return;
        float w = (float)nW.Value, h = (float)nH.Value, x = (float)nX.Value, y = (float)nY.Value;
        if (x + w > vw) x = vw - w;
        if (y + h > vh) y = vh - h;
        pic.Sel = new RectangleF(Math.Max(0, x), Math.Max(0, y), w, h);
        SyncNumerics(); pic.Invalidate();
    }

    void SetBusy(bool b)
    {
        busy = b; pnlRight.Enabled = !b; pnlLeft.Enabled = !b; btnSave.Enabled = !b; btnClose.Visible = !b; btnCancel.Visible = b;
    }

    void DoSave()
    {
        double s, e;
        if (!Ffmpeg.TryTime(txtStart.Text, out s) || !Ffmpeg.TryTime(txtEnd.Text, out e))
        { MessageBox.Show(this, "Please type the times like 00:01:23.5  (or just seconds, e.g. 83.5).", "Trim"); return; }
        if (e > dur) e = dur;
        if (e - s < 0.1) { MessageBox.Show(this, "The end time must be after the start time.", "Trim"); return; }
        bool crop = chkCrop.Checked && hasVideo, precise = hasVideo && (rbPrecise.Checked || crop);
        int cx = (int)nX.Value & ~1, cy = (int)nY.Value & ~1, cw = (int)nW.Value & ~1, ch = (int)nH.Value & ~1;
        if (crop)
        {
            if (cw < 16 || ch < 16) { MessageBox.Show(this, "The crop box is too small.", "Crop"); return; }
            if (cx + cw > vw) cx = (vw - cw) & ~1;
            if (cy + ch > vh) cy = (vh - ch) & ~1;
        }
        string ext = precise ? ".mp4" : Path.GetExtension(input);
        using (SaveFileDialog d = new SaveFileDialog())
        {
            d.Filter = (precise ? "MP4 video" : "Same format as the original") + "|*" + ext;
            d.FileName = Path.GetFileNameWithoutExtension(input) + (crop ? "_cropped" : "_trimmed");
            d.InitialDirectory = Path.GetDirectoryName(input); d.DefaultExt = ext.TrimStart('.'); d.AddExtension = true;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (string.Equals(Path.GetFullPath(d.FileName), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
            { MessageBox.Show(this, "Please choose a different file name. The original video is never overwritten.", "Save"); return; }
            jobOut = d.FileName;
        }
        int crf = cbQ.SelectedIndex == 0 ? 18 : (cbQ.SelectedIndex == 2 ? 27 : 22);
        jobLen = e - s;
        StartJob(Ffmpeg.EditArgs(input, jobOut, s, jobLen, hasVideo, crop, cx, cy, cw, ch, precise, chkMute.Checked, crf));
    }

    void StartJob(string args)
    {
        lock (errText) errText.Length = 0;
        cancelled = false;
        ProcessStartInfo psi = new ProcessStartInfo(ff, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        try
        {
            proc = new Process(); proc.StartInfo = psi; proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += OnOut; proc.ErrorDataReceived += OnErr; proc.Exited += OnExited;
            proc.Start(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not start ffmpeg:\n" + ex.Message, "Error"); proc = null; return; }
        SetBusy(true); prog.Value = 0; lblStatus.Text = "Working...  (the original file is not touched)";
    }
    void OnOut(object sender, DataReceivedEventArgs e)
    {
        string d = e.Data;
        if (d == null || jobLen <= 0) return;
        if (d.StartsWith("out_time_us=") || d.StartsWith("out_time_ms="))
        {
            double us;
            if (double.TryParse(d.Substring(d.IndexOf('=') + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out us))
            {
                double f = us / 1000000.0 / jobLen;
                if (f < 0) f = 0; if (f > 1) f = 1;
                int pc = (int)(f * 100);
                try { BeginInvoke(new MethodInvoker(delegate { prog.Value = pc; lblStatus.Text = "Working...  " + pc + " %"; })); } catch (Exception) { }
            }
        }
    }
    void OnErr(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (errText) errText.AppendLine(e.Data); }
    void OnExited(object sender, EventArgs e) { try { BeginInvoke(new MethodInvoker(JobDone)); } catch (Exception) { } }

    void JobDone()
    {
        int code = -1;
        try { proc.WaitForExit(); code = proc.ExitCode; } catch (Exception) { }
        try { proc.Dispose(); } catch (Exception) { }
        proc = null; SetBusy(false);
        if (cancelled) { try { File.Delete(jobOut); } catch (Exception) { } prog.Value = 0; lblStatus.Text = "Stopped."; return; }
        if (code == 0 && File.Exists(jobOut))
        {
            prog.Value = 100; SavedPath = jobOut;
            string sz = ""; try { sz = "  (" + (new FileInfo(jobOut).Length / 1048576.0).ToString("0.0") + " MB)"; } catch (Exception) { }
            lblStatus.Text = "Saved:  " + jobOut + sz;
            if (MessageBox.Show(this, "Done!\n\n" + jobOut + sz + "\n\nShow the new file in its folder?", "Saved", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            { try { Process.Start("explorer.exe", "/select,\"" + jobOut + "\""); } catch (Exception) { } }
        }
        else
        {
            string t; lock (errText) t = errText.ToString();
            if (t.Length > 700) t = t.Substring(t.Length - 700);
            prog.Value = 0; lblStatus.Text = "Failed.";
            MessageBox.Show(this, "ffmpeg could not finish this job.\n\n" + t, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try { File.Delete(jobOut); } catch (Exception) { }
        }
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (!busy) return;
        if (MessageBox.Show(this, "A video is still being saved. Stop it and close?", "Edit video", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
        cancelled = true; try { if (proc != null) proc.Kill(); } catch (Exception) { }
    }
}
