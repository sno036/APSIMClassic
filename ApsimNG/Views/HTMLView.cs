﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Gtk;
using WebKit;
using MonoMac.AppKit;
using APSIM.Shared.Utilities;

namespace UserInterface.Views
{
    /// <summary>
    /// An interface for a HTML view.
    /// </summary>
    interface IHTMLView
    {
        /// <summary>
        /// Path to find images on.
        /// </summary>
        string ImagePath { get; set; }

        /// <summary>
        /// Set the contents of the control. Can be RTF, HTML or MarkDown. If 
        /// the contents are markdown and 'allowModification' = true then
        /// user will be able to edit markdown.
        /// </summary>
        void SetContents(string contents, bool allowModification, bool isURI);

        /// <summary>
        /// Return the edited markdown.
        /// </summary>
        /// <returns></returns>
        string GetMarkdown();

        /// <summary>
        /// Tells view to use a mono spaced font.
        /// </summary>
        void UseMonoSpacedFont();

    }

    public interface IBrowserWidget : IDisposable
    {
        void Navigate(string uri);
        void LoadHTML(string html);

        /// <summary>
        /// Returns the Title of the current document
        /// </summary>
        /// <returns></returns>
        string GetTitle();

        void ExecJavaScript(string command, object[] args);
    }


    public class TWWebBrowserIE : IBrowserWidget
    {
        internal class NativeMethods
        {
            [System.Runtime.InteropServices.DllImportAttribute("user32.dll",
                EntryPoint = "SetParent")]
            internal static extern System.IntPtr
            SetParent([System.Runtime.InteropServices.InAttribute()] System.IntPtr
                hWndChild, [System.Runtime.InteropServices.InAttribute()] System.IntPtr
                hWndNewParent);
        }

        public System.Windows.Forms.WebBrowser wb = null;
        public Gtk.Socket socket = null;
        public bool unmapped = false;

        public void InitIE(Gtk.Box w)
        {
            wb = new System.Windows.Forms.WebBrowser();
            w.SetSizeRequest(500, 500);
            wb.Height = 500; // w.GdkWindow.FrameExtents.Height;
            wb.Width = 500; // w.GdkWindow.FrameExtents.Width;
            wb.Navigate("about:blank");
            wb.Document.Write(String.Empty);

            socket = new Gtk.Socket();
            socket.SetSizeRequest(wb.Width, wb.Height);
            w.Add(socket);
            socket.Realize();
            socket.Show();
            socket.UnmapEvent += Socket_UnmapEvent;
            IntPtr browser_handle = wb.Handle;
            IntPtr window_handle = (IntPtr)socket.Id;
            NativeMethods.SetParent(browser_handle, window_handle);

            /// Another interesting issue is that on Windows, the WebBrowser control by default is
            /// effectively an IE7 browser, and I don't think you can easily change that without
            /// changing registry settings. The lack of JSON parsing in IE7 triggers errors in google maps.
            /// See https://code.google.com/p/gmaps-api-issues/issues/detail?id=9004 for the details.
            /// Including the meta tag of <meta http-equiv="X-UA-Compatible" content="IE=edge"/>
            /// fixes the problem, but we can't do that in the HTML that we set as InnerHtml in the
            /// LoadHTML function, as the meta tag triggers a restart of the browser, so it 
            /// just reloads "about:blank", without the new innerHTML, and we get a blank browser.
            /// Hence we set the browser type here.
            /// Another way to get around this problem is to add JSON.Parse support available from
            /// https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/JSON
            /// into the HTML Script added when loading Google Maps
            /// I am taking the belts-and-braces approach of doing both, primarily because the 
            /// meta tag, while probably the technically better" solution, sometimes doesn't work.
            /// 10/8/17 - I've added yet another "fix" for this problem: the installer now writes a 
            /// registry key requesting that IE 11 be used for ApsimNG.exe (and for ApsimNG.vshost.exe,
            /// so it also works when run from Visual Studio).

            wb.DocumentText = @"<!DOCTYPE html>
                   <html>
                   <head>
                   <meta http-equiv=""X-UA-Compatible"" content=""IE=edge,10""/>
                   </head>
                   </html>";
        }

        public void Remap()
        {
            // There are some quirks in the use of GTK sockets. I don't know why, but
            // once the socket has been "unmapped", we seem to lose it and its content.
            // In the GUI, this unmapping can occur either by switching to another tab, 
            // or by shrinking the window dimensions down to 0.
            // This explict remapping seems to correct the problem.
            if (socket != null)
            {
                socket.Unmap();
                socket.Map();
            }
            unmapped = false;
        }

        internal void Socket_UnmapEvent(object o, UnmapEventArgs args)
        {
            unmapped = true;
        }

        public void Navigate(string uri)
        {
            wb.Navigate(uri);
        }

        public void LoadHTML(string html)
        {
            if (wb.Document.Body != null)
                // If we already have a document body, this is the more efficient
                // way to update its contents. It doesn't affect the scroll position
                // and doesn't make a little clicky sound.
                wb.Document.Body.InnerHtml = html;
            else
                wb.DocumentText = html;
            // Probably should make this conditional.
            // We use a timeout so we don't sit here forever if a document fails to load.

            Stopwatch watch = new Stopwatch();
            watch.Start();
            while (wb != null && wb.ReadyState != WebBrowserReadyState.Complete && watch.ElapsedMilliseconds < 10000)
                while (Gtk.Application.EventsPending())
                    Gtk.Application.RunIteration();
            wb.Document.BackColor = System.Drawing.Color.FromArgb(34, 34, 34);
            wb.Document.ForeColor = System.Drawing.Color.FromArgb(255, 255, 255);
        }

        public System.Drawing.Color BackgroundColour
        {
            get
            {
                return wb.Document.BackColor;
            }
            set
            {
                wb.Document.BackColor = value;
            }
        }

        public System.Drawing.Color ForegroundColour
        {
            get
            {
                return wb.Document.ForeColor;
            }
            set
            {
                wb.Document.ForeColor = value;
            }
        }

        public TWWebBrowserIE(Gtk.Box w)
        {
            InitIE(w);
        }

        public string GetTitle()
        {
            if (wb.Document != null)
                return wb.Document.Title;
            else
                return String.Empty;
        }
        public void ExecJavaScript(string command, object[] args)
        {
            wb.Document.InvokeScript(command, args);
        }

        // Flag: Has Dispose already been called? 
        bool disposed = false;

        // Public implementation of Dispose pattern callable by consumers. 
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern. 
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                wb.Dispose();
                wb = null;
                socket.Dispose();
                socket = null;
            }

            // Free any unmanaged objects here. 
            //
            disposed = true;
        }
    }

    public class TWWebBrowserSafari : IBrowserWidget
    {
        internal class NativeMethods
        {
            const string LIBQUARTZ = "libgtk-quartz-2.0.dylib";

            [DllImport(LIBQUARTZ)]
            internal static extern IntPtr gdk_quartz_window_get_nsview(IntPtr window);

            [DllImport(LIBQUARTZ)]
            internal static extern IntPtr gdk_quartz_window_get_nswindow(IntPtr window);

            [DllImport(LIBQUARTZ, CallingConvention = CallingConvention.Cdecl)]
            internal static extern bool gdk_window_supports_nsview_embedding();

            [DllImport(LIBQUARTZ)]
            internal extern static IntPtr gtk_ns_view_new(IntPtr nsview);
        }

        public static Gtk.Widget NSViewToGtkWidget(NSView view)
        {
            return new Gtk.Widget(NativeMethods.gtk_ns_view_new((IntPtr)view.Handle));
        }

        public static NSWindow GetWindow(Gtk.Window window)
        {
            if (window.GdkWindow == null)
                return null;
            var ptr = NativeMethods.gdk_quartz_window_get_nswindow(window.GdkWindow.Handle);
            if (ptr == IntPtr.Zero)
                return null;
            return (NSWindow)MonoMac.ObjCRuntime.Runtime.GetNSObject(ptr);
        }

        public static NSView GetView(Gtk.Widget widget)
        {
            var ptr = NativeMethods.gdk_quartz_window_get_nsview(widget.GdkWindow.Handle);
            if (ptr == IntPtr.Zero)
                return null;
            return (NSView)MonoMac.ObjCRuntime.Runtime.GetNSObject(ptr);
        }

        public MonoMac.WebKit.WebView wb = null;
        public Gtk.Socket socket = new Gtk.Socket();
        public ScrolledWindow scrollWindow = new ScrolledWindow();

        public void InitWebKit(Gtk.Box w)
        {
            wb = new MonoMac.WebKit.WebView(new System.Drawing.RectangleF(10, 10, 200, 200), "foo", "bar");
            scrollWindow.AddWithViewport(NSViewToGtkWidget(wb));
            w.PackStart(scrollWindow, true, true, 0);
            w.ShowAll();
            wb.ShouldCloseWithWindow = true;
        }

        public void Navigate(string uri)
        {
            wb.MainFrame.LoadRequest(new MonoMac.Foundation.NSUrlRequest(new MonoMac.Foundation.NSUrl(uri)));
        }

        public void LoadHTML(string html)
        {
            wb.MainFrame.LoadHtmlString(html, new MonoMac.Foundation.NSUrl("about:blank"));
            // Probably should make this conditional.
            // We use a timeout so we don't sit here forever if a document fails to load.
			Stopwatch watch = new Stopwatch();
			watch.Start();
			while (wb != null && wb.IsLoading && watch.ElapsedMilliseconds < 10000)
				while (Gtk.Application.EventsPending())
					Gtk.Application.RunIteration();
        }

        public TWWebBrowserSafari(Gtk.Box w)
        {
            InitWebKit(w);
        }

        public string GetTitle()
        {
            return wb.MainFrameTitle;
        }

        public void ExecJavaScript(string command, object[] args)
        {
            string argString = "";
            bool first = true;
            foreach (object obj in args)
            {
                if (!first)
                    argString += ", ";
                first = false;
                argString += obj.ToString();
            }
            wb.StringByEvaluatingJavaScriptFromString(command + "(" + argString + ");");
        }

        // Flag: Has Dispose already been called? 
        bool disposed = false;

        // Public implementation of Dispose pattern callable by consumers. 
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern. 
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                wb.Dispose();
                wb = null;
                socket.Dispose();
                socket = null;
                scrollWindow.Destroy();
            }

            // Free any unmanaged objects here. 
            //
            disposed = true;
        }
    }

    public class TWWebBrowserWK : IBrowserWidget
    {
        public WebKit.WebView wb = null;
        public ScrolledWindow scrollWindow = new ScrolledWindow();

        public void InitWebKit(Gtk.Box w)
        {
            wb = new WebKit.WebView();
            scrollWindow.Add(wb);
            // Hack to work around webkit bug; webkit will crash the app if a size is not provided
            // See https://bugs.eclipse.org/bugs/show_bug.cgi?id=466360 for a related bug report
            wb.SetSizeRequest(2, 2);
            w.PackStart(scrollWindow, true, true, 0);
            w.ShowAll();
        }

        public void Navigate(string uri)
        {
            wb.Open(uri);
        }

        public void LoadHTML(string html)
        {
            wb.LoadHtmlString(html, "about:blank");
            // Probably should make this conditional.
            // We use a timeout so we don't sit here forever if a document fails to load.

            Stopwatch watch = new Stopwatch();
            watch.Start();
            while (wb != null && wb.LoadStatus != LoadStatus.Finished && watch.ElapsedMilliseconds < 10000)
                while (Gtk.Application.EventsPending())
                    Gtk.Application.RunIteration();
        }

        public TWWebBrowserWK(Gtk.Box w)
        {
            InitWebKit(w);
        }

        public string GetTitle()
        {
            return wb.Title;
        }

        public void ExecJavaScript(string command, object[] args)
        {
            string argString = "";
            bool first = true;
            foreach (object obj in args)
            {
                if (!first)
                    argString += ", ";
                first = false;
                argString += obj.ToString();
            }
            wb.ExecuteScript(command + "(" + argString + ")");
        }
        // Flag: Has Dispose already been called? 
        bool disposed = false;

        // Public implementation of Dispose pattern callable by consumers. 
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern. 
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                wb.Dispose();
                wb = null;
                scrollWindow.Destroy();
            }

            // Free any unmanaged objects here. 
            //
            disposed = true;
        }
    }

    /// <summary>
    /// The Presenter for a HTML component.
    /// </summary>
    public class HTMLView : ViewBase, IHTMLView
    {
        /// <summary>
        /// Path to find images on.
        /// </summary>
        public string ImagePath { get; set; }

        private VPaned vpaned1 = null;
        private VBox vbox2 = null;
        private Frame frame1 = null;
        private HBox hbox1 = null;

        protected IBrowserWidget browser = null;
        private MemoView memoView1;
        protected Gtk.Window popupWin = null;

        /// <summary>
        /// Constructor
        /// </summary>
        public HTMLView(ViewBase owner) : base(owner)
        {
            Builder builder = MasterView.BuilderFromResource("ApsimNG.Resources.Glade.HTMLView.glade");
            vpaned1 = (VPaned)builder.GetObject("vpaned1");
            vbox2 = (VBox)builder.GetObject("vbox2");
            frame1 = (Frame)builder.GetObject("frame1");
            hbox1 = (HBox)builder.GetObject("hbox1");
            _mainWidget = vpaned1;
            // Handle a temporary browser created when we want to export a map.
            if (owner == null)
            {
                popupWin = new Gtk.Window(Gtk.WindowType.Popup);
                popupWin.SetSizeRequest(500, 500);
                // Move the window offscreen; the user doesn't need to see it.
                // This works with IE, but not with WebKit
                // Not yet tested on OSX
                if (ProcessUtilities.CurrentOS.IsWindows)
                    popupWin.Move(-10000, -10000);
                popupWin.Add(MainWidget);
                popupWin.ShowAll();
                while (Gtk.Application.EventsPending())
                    Gtk.Application.RunIteration();
            }
            memoView1 = new MemoView(this);
            hbox1.PackStart(memoView1.MainWidget, true, true, 0);
            vpaned1.PositionSet = true;
            vpaned1.Position = 200;
            hbox1.Visible = false;
            hbox1.NoShowAll = true;
            memoView1.ReadOnly = false;
            memoView1.WordWrap = true;
            memoView1.MemoChange += this.TextUpdate;
            vpaned1.ShowAll();
            frame1.ExposeEvent += OnWidgetExpose;
            hbox1.Realized += Hbox1_Realized;
            _mainWidget.Destroyed += _mainWidget_Destroyed;
        }

        protected void _mainWidget_Destroyed(object sender, EventArgs e)
        {
            memoView1.MemoChange -= this.TextUpdate;
            frame1.ExposeEvent -= OnWidgetExpose;
            hbox1.Realized -= Hbox1_Realized;
            if ((browser as TWWebBrowserIE) != null)
            {
                if (vbox2.Toplevel is Window)
                    (vbox2.Toplevel as Window).SetFocus -= MainWindow_SetFocus;
                frame1.Unrealized -= Frame1_Unrealized;
                (browser as TWWebBrowserIE).socket.UnmapEvent -= (browser as TWWebBrowserIE).Socket_UnmapEvent;
            }
            if (browser != null)
                browser.Dispose();
            if (popupWin != null)
            {
                popupWin.Destroy();
            }
            memoView1.MainWidget.Destroy();
            memoView1 = null;
            _mainWidget.Destroyed -= _mainWidget_Destroyed;
            _owner = null;
        }

        private void Hbox1_Realized(object sender, EventArgs e)
        {
            vpaned1.Position = vpaned1.Parent.Allocation.Height / 2;
        }

        private void Frame1_Unrealized(object sender, EventArgs e)
        {
            if ((browser as TWWebBrowserIE) != null)
                (vbox2.Toplevel as Window).SetFocus -= MainWindow_SetFocus;
        }

        private void MainWindow_SetFocus(object o, SetFocusArgs args)
        {
            if (MasterView.MainWindow != null)
                MasterView.MainWindow.Focus(0);
        }

        /// <summary>
        /// Set the contents of the control. Can be RTF, HTML or MarkDown. If 
        /// the contents are markdown and 'allowModification' = true then
        /// user will be able to edit markdown.
        /// </summary>
        public void SetContents(string contents, bool allowModification, bool isURI=false)
        {
            TurnEditorOn(allowModification);
            if (contents != null)
            {
                if (allowModification)
                    memoView1.MemoText = contents;
                else
                    PopulateView(contents, isURI);
            }
        }

        /// <summary>
        /// Populate the view given the specified text.
        /// </summary>
        /// <param name="contents"></param>
        /// <param name="editingEnabled"></param>
        /// <returns></returns>
        private void PopulateView(string contents, bool isURI= false)
        {
            if (browser == null)
            {
                if (ProcessUtilities.CurrentOS.IsWindows)
                {
                    browser = CreateIEBrowser(vbox2);

                    /// UGH! Once the browser control gets keyboard focus, it doesn't relinquish it to 
                    /// other controls. It's actually a COM object, I guess, and running
                    /// with a different message loop, and probably in a different thread. 
                    /// 
                    /// Well, this hack works, more or less.
                    if (vbox2.Toplevel is Window)
                        (vbox2.Toplevel as Window).SetFocus += MainWindow_SetFocus;
                    frame1.Unrealized += Frame1_Unrealized;
                    if (this is MapView) // If we're only displaying a map, remove the unneeded scrollbar
                       (browser as TWWebBrowserIE).wb.ScrollBarsEnabled = false;
                }
                else if (ProcessUtilities.CurrentOS.IsMac)
                {
                    browser = CreateSafariBrowser(vbox2);
                }
                else
                {
                    browser = CreateWebKitBrowser(vbox2);
                }
            }
            if (isURI)
                browser.Navigate(contents);
            else
               browser.LoadHTML(contents);
            if (browser is TWWebBrowserIE)
            {
                Gdk.Color colour = MainWidget.Style.Base(StateType.Normal);
                (browser as TWWebBrowserIE).BackgroundColour = System.Drawing.Color.FromArgb(colour.Red * 255 / 65535, colour.Green * 255 / 65535, colour.Blue * 255 / 65535);
                colour = MainWidget.Style.Foreground(StateType.Normal);
                (browser as TWWebBrowserIE).ForegroundColour = System.Drawing.Color.FromArgb(colour.Red * 255 / 65535, colour.Green * 255 / 65535, colour.Blue * 255 / 65535);
            }
        }

        private IBrowserWidget CreateIEBrowser(Gtk.Box box)
        {
            return new TWWebBrowserIE(box);
        }

        private IBrowserWidget CreateSafariBrowser(Gtk.Box box)
        {
            return new TWWebBrowserSafari(box);
        }

        private IBrowserWidget CreateWebKitBrowser(Gtk.Box box)
        {
            return new TWWebBrowserWK(box);
        }

        protected virtual void NewTitle(string title)
        {
        }

        // Although this isn't the obvious way to handle window resizing,
        // I couldn't find any better technique. 
        public void OnWidgetExpose(object o, ExposeEventArgs args)
        {
            int height, width;
            frame1.GdkWindow.GetSize(out width, out height);
            frame1.SetSizeRequest(width, height);
            if (browser is TWWebBrowserIE)
            {
                TWWebBrowserIE brow = browser as TWWebBrowserIE;
                if (brow.unmapped)
                {
                    brow.Remap();
                }

                if (brow.wb.Height != height || brow.wb.Width != width)
                {
                    brow.socket.SetSizeRequest(width, height);
                    brow.wb.Height = height;
                    brow.wb.Width = width;
                }
            }
        }

        /// <summary>
        /// Return the edited markdown.
        /// </summary>
        /// <returns></returns>
        public string GetMarkdown()
        {
            return memoView1.MemoText;
        }

        /// <summary>
        /// Tells view to use a mono spaced font.
        /// </summary>
        public void UseMonoSpacedFont()
        {
        }

        /// <summary>
        /// Turn the editor on or off.
        /// </summary>
        /// <param name="turnOn"></param>
        private void TurnEditorOn(bool turnOn)
        {
            hbox1.Visible = turnOn;
        }

        /// <summary>
        /// Toggle edit / preview mode.
        /// </summary>
        private void ToggleEditMode()
        {
            bool editorIsOn = hbox1.Visible;
            TurnEditorOn(!editorIsOn);   // toggle preview / edit mode.
        }

        #region Event Handlers

        /// <summary>
        /// User has clicked 'edit'
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnEditClick(object sender, EventArgs e)
        {
            TurnEditorOn(true);
        }

        private void TextUpdate(object sender, EventArgs e)
        {
            MarkdownDeep.Markdown markDown = new MarkdownDeep.Markdown();
            markDown.ExtraMode = true;
            string html = markDown.Transform(memoView1.MemoText);
            PopulateView(html);
        }

        #endregion

        private void OnHelpClick(object sender, EventArgs e)
        {
            Process.Start("https://www.apsim.info/Documentation/APSIM(nextgeneration)/Memo.aspx");
        }

        public void EnableWb(bool state)
        {
            if (browser is TWWebBrowserIE)
                (browser as TWWebBrowserIE).wb.Parent.Enabled = state;
        }

    }
}
