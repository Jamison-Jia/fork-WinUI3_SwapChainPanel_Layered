// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

using DXGI;
using GlobalStructures;
using Direct2D;
using static DXGI.DXGITools;
using GDIPlus;
using static GDIPlus.GDIPlusTools;
using static WinUI3_SwapChainPanel_Layered.MainWindow;
using Microsoft.Win32;
using System.Text;
using WinUI3_SwapChainPanel_Layered.Helpers;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUI3_SwapChainPanel_Layered
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        #region Fields

        //private SUBCLASSPROC SubClassDelegate;

        private IntPtr hWndMain = IntPtr.Zero;
        private IntPtr hWndDesktopChildSiteBridge = IntPtr.Zero;
        private Microsoft.UI.Windowing.AppWindow _apw;
        private Microsoft.UI.Windowing.OverlappedPresenter _presenter;


        IntPtr m_initToken = IntPtr.Zero;
        IntPtr m_hBitmap = IntPtr.Zero;

        ID2D1Factory m_pD2DFactory = null;
        ID2D1Factory1 m_pD2DFactory1 = null;

        IntPtr m_pD3D11DevicePtr = IntPtr.Zero;
        ID3D11DeviceContext m_pD3D11DeviceContext = null;
        IDXGIDevice1 m_pDXGIDevice = null;

        ID2D1DeviceContext m_pD2DDeviceContext = null;

        //ID2D1Bitmap1 m_pD2DTargetBitmap = null;
        IDXGISwapChain1 m_pDXGISwapChain1 = null;

        private NativeHelpers _nativeHelpers = new();

        #endregion


        public MainWindow()
        {
            this.InitializeComponent();

            HRESULT hr = HRESULT.S_OK;

            //Application.Current.Resources["ButtonBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            Application.Current.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Microsoft.UI.Colors.LightSteelBlue);
            Application.Current.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.RoyalBlue);    

            hWndMain = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId myWndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWndMain);
            _apw = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(myWndId);

            hWndDesktopChildSiteBridge = NativeHelpers.FindWindowEx(hWndMain, IntPtr.Zero, "Microsoft.UI.Content.ContentWindowSiteBridge", null);

            _presenter = _apw.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            _presenter.IsResizable = false;
            //_presenter.IsResizable = true;
            _presenter.SetBorderAndTitleBar(false, false);
            //_presenter.SetBorderAndTitleBar(true, false);
            //
            //this.ExtendsContentIntoTitleBar = true;
            //_presenter.IsAlwaysOnTop = true;

            _apw.Resize(new Windows.Graphics.SizeInt32(800, 500));
            _apw.Move(new Windows.Graphics.PointInt32(500, 300));

            // Update for Windows 11 from michalleptuch comment : https://github.com/microsoft/microsoft-ui-xaml/issues/1247#issuecomment-1374474960
            // otherwise there are borders + shadow from his test
            // Returns logically 0x80070057 (E_INVALIDARG) on Windows 10
            int nValue = (int)NativeHelpers.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DEFAULT;
            hr = NativeHelpers.DwmSetWindowAttribute(hWndMain, (int)NativeHelpers.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref nValue, Marshal.SizeOf(typeof(int)));

            //nValue = (int)DWMNCRENDERINGPOLICY.DWMNCRP_DISABLED;
            //hr = DwmSetWindowAttribute(hWndMain, (int)DWMWINDOWATTRIBUTE.DWMWA_NCRENDERING_POLICY, ref nValue, Marshal.SizeOf(typeof(int)));

            this.Closed += MainWindow_Closed;

            StartupInput input = StartupInput.GetDefault();
            StartupOutput output;           
            GpStatus nStatus = GdiplusStartup(out m_initToken, ref input, out output);
 
            IntPtr pImage = IntPtr.Zero;
            nStatus = GdipCreateBitmapFromFile(@".\Assets\Frame_Blue_center_transp.png", out pImage);
            if (nStatus == GpStatus.Ok)
            {
                GdipCreateHBITMAPFromBitmap(pImage, out m_hBitmap, RGB(Microsoft.UI.Colors.Black.R, Microsoft.UI.Colors.Black.G, Microsoft.UI.Colors.Black.B));
                GdipDisposeImage(pImage);
            }

            hr = CreateD2D1Factory();
            if (hr == HRESULT.S_OK)
            {
                hr = CreateDeviceContext();
                // hr = CreateDeviceResources();
                // hr = CreateSwapChain(hWndMain);
                hr = CreateSwapChain(IntPtr.Zero);
                if (hr == HRESULT.S_OK)
                {
                    //hr = ConfigureSwapChain();
                    ISwapChainPanelNative panelNative = WinRT.CastExtensions.As<ISwapChainPanelNative>(swapChainPanel1);
                    hr = panelNative.SetSwapChain(m_pDXGISwapChain1);
                    //swapChainPanel1.SizeChanged += SwapChainPanel1_SizeChanged;
                }
                //CompositionTarget.Rendering += CompositionTarget_Rendering;
            }

            long nExStyle = NativeHelpers.GetWindowLong(hWndMain, NativeHelpers.GWL_EXSTYLE);
            if ((nExStyle & NativeHelpers.WS_EX_LAYERED) == 0)
            {
                NativeHelpers.SetWindowLong(hWndMain, NativeHelpers.GWL_EXSTYLE, (IntPtr)(nExStyle | NativeHelpers.WS_EX_LAYERED));
                //SetWindowLong(hWndMain, GWL_EXSTYLE, (IntPtr)(nExStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT));

                // Test Light Mode
                int nAppsUseLightTheme = 0;
                int nSystemUsesLightTheme = 0;
                string sPathKey = @"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
                using (RegistryKey rkLocal = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    using (RegistryKey rk = rkLocal.OpenSubKey(sPathKey, false))
                    {
                        nAppsUseLightTheme = (int)rk.GetValue("AppsUseLightTheme", 0);
                        nSystemUsesLightTheme = (int)rk.GetValue("SystemUsesLightTheme", 0);
                    }
                }
                uint nColorBackground = (uint)System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.Black);
                //if (nAppsUseLightTheme == 1 || nSystemUsesLightTheme == 1)
                if (nAppsUseLightTheme == 1)
                {
                    nColorBackground = (uint)System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.White);
                    // not refreshed when mouse over...
                    // myButton.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                }
                //bool bReturn = SetLayeredWindowAttributes(hWndMain, nColorBackground, 55, LWA_COLORKEY | LWA_ALPHA);

                // For click-through
                bool bReturn = NativeHelpers.SetLayeredWindowAttributes(hWndMain, nColorBackground, 255, NativeHelpers.LWA_COLORKEY );

                //SystemBackdrop = new TransparentBackdrop();
            }

            //nExStyle = GetWindowLong(hWndMain, GWL_EXSTYLE);
            //SetWindowLong(hWndMain, GWL_EXSTYLE, (IntPtr)(nExStyle | (WS_EX_NOACTIVATE | WS_EX_APPWINDOW)));

            // From https://github.com/castorix/WinUI3_SwapChainPanel_Layered/issues/7
            int nStyle = (int)NativeHelpers.GetWindowLong(hWndMain, NativeHelpers.GWL_STYLE);
            nStyle = nStyle & ~(NativeHelpers.WS_CAPTION | NativeHelpers.WS_THICKFRAME); // or WS_DLGFRAME ?
            NativeHelpers.SetWindowLong(hWndMain, NativeHelpers.GWL_STYLE, (IntPtr)nStyle);

            UIElement root = (UIElement)this.Content;         
            root.PointerMoved += Root_PointerMoved;
            root.PointerPressed += Root_PointerPressed;
            root.PointerReleased += Root_PointerReleased;

            //SubClassDelegate = new SUBCLASSPROC(WindowSubClass);
            //bool bRet = SetWindowSubclass(hWndMain, SubClassDelegate, 0, 0);
            
            // Test TopMost
            //_presenter.IsAlwaysOnTop = true;
        }

        private void tsClickThrough_Toggled(object sender, RoutedEventArgs e)
        {
            ToggleSwitch ts = sender as ToggleSwitch;
            if (ts.IsOn)
            {
                tb2.Visibility = Visibility.Visible;
                tb3.Visibility = Visibility.Visible;
            }
            else
            {
                tb2.Visibility = Visibility.Collapsed;
                tb3.Visibility = Visibility.Collapsed;
            }
        }

        public void SetOpacity(IntPtr hWnd, int nOpacity)
        {
            NativeHelpers.SetLayeredWindowAttributes(hWnd, 0, (byte)(255 * nOpacity / 100), NativeHelpers.LWA_ALPHA);
        }

        bool bSet = false;
        private void myButton_Click(object sender, RoutedEventArgs e)
        {           
            if (m_hBitmap != IntPtr.Zero && !bSet)
            {
                NativeHelpers.SetWindowLong(hWndMain, NativeHelpers.GWL_EXSTYLE, (IntPtr)(NativeHelpers.GetWindowLong(hWndMain, NativeHelpers.GWL_EXSTYLE) & ~NativeHelpers.WS_EX_LAYERED));
                NativeHelpers.RedrawWindow(hWndMain, IntPtr.Zero, IntPtr.Zero, NativeHelpers.RDW_ERASE | NativeHelpers.RDW_INVALIDATE | NativeHelpers.RDW_FRAME | NativeHelpers.RDW_ALLCHILDREN);
                NativeHelpers.SetWindowLong(hWndMain, NativeHelpers.GWL_EXSTYLE, (IntPtr)(NativeHelpers.GetWindowLong(hWndMain, NativeHelpers.GWL_EXSTYLE) | NativeHelpers.WS_EX_LAYERED));
                if (SetPictureToLayeredWindow(hWndMain, m_hBitmap))
                {                    
                    mainBorder.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    tb1.Margin = new Thickness(5, 100, 5, 5);
                    myButton.Margin = new Thickness(10, 10, 10, 10);
                    myButton.Content = "Bitmap set";
                    //RedrawWindow(hWndMain, IntPtr.Zero, IntPtr.Zero, RDW_ERASE | RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW | RDW_ERASENOW);
                    RECT rectWnd;
                    NativeHelpers.GetWindowRect(hWndMain, out rectWnd);
                    NativeHelpers.SetWindowPos(hWndMain, IntPtr.Zero, rectWnd.left, rectWnd.top - 1, 0, 0, NativeHelpers.SWP_NOSIZE | NativeHelpers.SWP_NOZORDER | NativeHelpers.SWP_SHOWWINDOW | NativeHelpers.SWP_FRAMECHANGED);
                    NativeHelpers.SetWindowPos(hWndMain, IntPtr.Zero, rectWnd.left, rectWnd.top, 0, 0, NativeHelpers.SWP_NOSIZE | NativeHelpers.SWP_NOZORDER | NativeHelpers.SWP_SHOWWINDOW | NativeHelpers.SWP_FRAMECHANGED);
                    bSet = true;
                }
            }
        }

        private int nX = 0, nY = 0, nXWindow = 0, nYWindow = 0;
        private bool bMoving = false;

        #region Mouse Interactions

        private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ((UIElement)sender).ReleasePointerCaptures();
            bMoving = false;
        }

        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsLeftButtonPressed)
            {
                ((UIElement)sender).CapturePointer(e.Pointer);
                nXWindow = _apw.Position.X;
                nYWindow = _apw.Position.Y;
                Windows.Graphics.PointInt32 pt;
                NativeHelpers.GetCursorPos(out pt);
                nX = pt.X;
                nY = pt.Y;

                //IntPtr hDCScreen = GetDC(IntPtr.Zero);
                //uint nColor = GetPixel(hDCScreen, nX, nY);
                //ReleaseDC(IntPtr.Zero, hDCScreen);

                IntPtr hWnd = NativeHelpers.WindowFromPoint(pt);

                StringBuilder sbClass = new StringBuilder(260);
                NativeHelpers.GetClassName(hWnd, sbClass, (int)(sbClass.Capacity));
                IntPtr hWndParent = NativeHelpers.GetParent(hWnd);
                StringBuilder sbParentClass = new StringBuilder(260);
                NativeHelpers.GetClassName(hWndParent, sbParentClass, (int)(sbParentClass.Capacity));
                
                //System.Diagnostics.Debug.WriteLine(string.Format("Window = 0x{0:X8} - {1}", hWnd, sbClass.ToString()));

                Microsoft.UI.Input.PointerPoint pp = e.GetCurrentPoint((UIElement)sender);
                Point ptElement = new Point(pp.Position.X, pp.Position.Y);
                IEnumerable<UIElement> elementStack = VisualTreeHelper.FindElementsInHostCoordinates(ptElement, (UIElement)sender);
                int nCpt = 0;
                bool bOK = true;
                foreach (UIElement element in elementStack)
                {
                    if (nCpt == 0)
                    {
                        if (!(element.GetType() == typeof(Border)))
                        {
                            bOK = false;
                            break;                       
                        }
                    }
                    if (nCpt == 1)
                    {
                        if (!(element.GetType() == typeof(SwapChainPanel)))
                        {
                            bOK = false;
                            break;
                        }
                    }
                    nCpt++;
                }                

                if (bOK && tsClickThrough.IsOn)
                {
                    bool bAlwaysOnTop = false;
                    if (_presenter.IsAlwaysOnTop)
                    {
                        bAlwaysOnTop = true;
                        _presenter.IsAlwaysOnTop = false;                       
                    }
                    System.Threading.Thread.Sleep(100);
                    NativeHelpers.SwitchToThisWindow(hWnd, true);
                    System.Threading.Thread.Sleep(100);
                    NativeHelpers.INPUT[] mi = new NativeHelpers.INPUT[1];
                    mi[0].type = NativeHelpers.INPUT_MOUSE;                   
                    mi[0].inputUnion.mi.dwFlags = NativeHelpers.MOUSEEVENTF_LEFTDOWN;
                    NativeHelpers.SendInput(1, mi, Marshal.SizeOf(mi[0]));
                    //System.Threading.Thread.Sleep(100);
                    mi[0].inputUnion.mi.dwFlags = NativeHelpers.MOUSEEVENTF_LEFTUP;
                    NativeHelpers.SendInput(1, mi, Marshal.SizeOf(mi[0]));

                    // Test Desktop (Windows 10)
                    if (sbClass.ToString() == "SysListView32" && sbParentClass.ToString() == "SHELLDLL_DefView")
                    {
                        _presenter.Minimize();
                    }

                    //Console.Beep(5000, 10);
                    if (bAlwaysOnTop)
                        _presenter.IsAlwaysOnTop = true;
                }
                else
                    bMoving = true;
                //((UIElement)sender).ReleasePointerCapture(e.Pointer);

                //MSG msg = new MSG();
                //while (PeekMessage(out msg, IntPtr.Zero, WM_LBUTTONDOWN, WM_LBUTTONUP, PM_REMOVE));
                
            }
            else if (properties.IsRightButtonPressed)
            {
                System.Threading.Thread.Sleep(200);
                Application.Current.Exit();
            }
        }

        private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
        {  
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsLeftButtonPressed)
            {
                Windows.Graphics.PointInt32 pt;
                NativeHelpers.GetCursorPos(out pt);
                if (bMoving)
                    _apw.Move(new Windows.Graphics.PointInt32(nXWindow + (pt.X - nX), nYWindow + (pt.Y - nY)));               
                e.Handled = true;
            }
        }

        #endregion

        public HRESULT CreateD2D1Factory()
        {
            HRESULT hr = HRESULT.S_OK;
            D2D1_FACTORY_OPTIONS options = new D2D1_FACTORY_OPTIONS();

            // Needs "Enable native code Debugging"
            options.debugLevel = D2D1_DEBUG_LEVEL.D2D1_DEBUG_LEVEL_INFORMATION;

            hr = D2DTools.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, ref D2DTools.CLSID_D2D1Factory, ref options, out m_pD2DFactory);
            m_pD2DFactory1 = (ID2D1Factory1)m_pD2DFactory;
            return hr;
        }

        public HRESULT CreateDeviceContext()
        {
            HRESULT hr = HRESULT.S_OK;
            uint creationFlags = (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;

            // Needs "Enable native code Debugging"
            creationFlags |= (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG;

            int[] aD3D_FEATURE_LEVEL = new int[] { (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1};

            D3D_FEATURE_LEVEL featureLevel;
            hr = D2DTools.D3D11CreateDevice(null,    // specify null to use the default adapter
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                creationFlags,      // optionally set debug and Direct2D compatibility flags
                aD3D_FEATURE_LEVEL, // list of feature levels this app can support
                // (uint)Marshal.SizeOf(aD3D_FEATURE_LEVEL),   // number of possible feature levels
                (uint)aD3D_FEATURE_LEVEL.Length, // number of possible feature levels
                D2DTools.D3D11_SDK_VERSION,
                out m_pD3D11DevicePtr,    // returns the Direct3D device created
                out featureLevel,         // returns feature level of device created            
                out m_pD3D11DeviceContext // returns the device immediate context
            );
            if (hr == HRESULT.S_OK)
            {
                m_pDXGIDevice = Marshal.GetObjectForIUnknown(m_pD3D11DevicePtr) as IDXGIDevice1;
                if (m_pD2DFactory1 != null)
                {
                    ID2D1Device pD2DDevice = null;
                    hr = m_pD2DFactory1.CreateDevice(m_pDXGIDevice, out pD2DDevice);
                    if (hr == HRESULT.S_OK)
                    {
                        hr = pD2DDevice.CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE, out m_pD2DDeviceContext);
                        GlobalTools.SafeRelease(ref pD2DDevice);
                    }
                }
                //Marshal.ReleaseComObject(m_pDXGIDevice);
                //Marshal.Release(m_pD3D11DevicePtr);
            }
            return hr;
        }

        HRESULT CreateSwapChain(IntPtr hWnd)
        {
            HRESULT hr = HRESULT.S_OK;
            DXGI_SWAP_CHAIN_DESC1 swapChainDesc = new DXGI_SWAP_CHAIN_DESC1();
            swapChainDesc.Width = 1;
            swapChainDesc.Height = 1;
            swapChainDesc.Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM; // this is the most common swapchain format
            swapChainDesc.Stereo = false;
            swapChainDesc.SampleDesc.Count = 1;                // don't use multi-sampling
            swapChainDesc.SampleDesc.Quality = 0;
            swapChainDesc.BufferUsage = D2DTools.DXGI_USAGE_RENDER_TARGET_OUTPUT;
            swapChainDesc.BufferCount = 2;                     // use double buffering to enable flip
            swapChainDesc.Scaling = (hWnd != IntPtr.Zero) ? DXGI_SCALING.DXGI_SCALING_NONE : DXGI_SCALING.DXGI_SCALING_STRETCH;
            swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; // all apps must use this SwapEffect       
            swapChainDesc.Flags = 0;

            IDXGIAdapter pDXGIAdapter;
            hr = m_pDXGIDevice.GetAdapter(out pDXGIAdapter);
            if (hr == HRESULT.S_OK)
            {
                IntPtr pDXGIFactory2Ptr;
                hr = pDXGIAdapter.GetParent(typeof(IDXGIFactory2).GUID, out pDXGIFactory2Ptr);
                if (hr == HRESULT.S_OK)
                {
                    IDXGIFactory2 pDXGIFactory2 = Marshal.GetObjectForIUnknown(pDXGIFactory2Ptr) as IDXGIFactory2;
                    if (hWnd != IntPtr.Zero)
                        hr = pDXGIFactory2.CreateSwapChainForHwnd(m_pD3D11DevicePtr, hWnd, ref swapChainDesc, IntPtr.Zero, null, out m_pDXGISwapChain1);
                    else
                        hr = pDXGIFactory2.CreateSwapChainForComposition(m_pD3D11DevicePtr, ref swapChainDesc, null, out m_pDXGISwapChain1);

                    hr = m_pDXGIDevice.SetMaximumFrameLatency(1);
                    GlobalTools.SafeRelease(ref pDXGIFactory2);
                    Marshal.Release(pDXGIFactory2Ptr);
                }
                GlobalTools.SafeRelease(ref pDXGIAdapter);
            }
            return hr;
        }

        private bool SetPictureToLayeredWindow(IntPtr hWnd, IntPtr hBitmap)
        {
            NativeHelpers.BITMAP bm;
            NativeHelpers.GetObject(hBitmap, Marshal.SizeOf(typeof(NativeHelpers.BITMAP)), out bm);
            System.Drawing.Size sizeBitmap = new System.Drawing.Size(bm.bmWidth, bm.bmHeight);

            IntPtr hDCScreen = NativeHelpers.GetDC(IntPtr.Zero);
            IntPtr hDCMem = NativeHelpers.CreateCompatibleDC(hDCScreen);
            IntPtr hBitmapOld = NativeHelpers.SelectObject(hDCMem, hBitmap);

            NativeHelpers.BLENDFUNCTION bf = new NativeHelpers.BLENDFUNCTION();
            bf.BlendOp = NativeHelpers.AC_SRC_OVER;
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = NativeHelpers.AC_SRC_ALPHA;

            RECT rectWnd;
            NativeHelpers.GetWindowRect(hWnd, out rectWnd);

            System.Drawing.Point ptSrc = new System.Drawing.Point();
            System.Drawing.Point ptDest = new System.Drawing.Point(rectWnd.left, rectWnd.top);

            IntPtr pptSrc = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(System.Drawing.Point)));
            Marshal.StructureToPtr(ptSrc, pptSrc, false);

            IntPtr pptDest = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(System.Drawing.Point)));
            Marshal.StructureToPtr(ptDest, pptDest, false);

            IntPtr psizeBitmap = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(System.Drawing.Size)));
            Marshal.StructureToPtr(sizeBitmap, psizeBitmap, false);

            bool bRet = NativeHelpers.UpdateLayeredWindow(hWnd, hDCScreen, pptDest, psizeBitmap, hDCMem, pptSrc, 0, ref bf, NativeHelpers.ULW_ALPHA);
            //int nErr = Marshal.GetLastWin32Error();

            Marshal.FreeHGlobal(pptSrc);
            Marshal.FreeHGlobal(pptDest);
            Marshal.FreeHGlobal(psizeBitmap);

            NativeHelpers.SelectObject(hDCMem, hBitmapOld);
            NativeHelpers.DeleteDC(hDCMem);
            NativeHelpers.ReleaseDC(IntPtr.Zero, hDCScreen);

            return bRet;
        }

        void Clean()
        {
            GlobalTools.SafeRelease(ref m_pD2DDeviceContext);
            //GlobalTools.SafeRelease(ref m_pD2DDeviceContext3);

            //CleanDeviceResources();

            //GlobalTools.SafeRelease(ref m_pD2DTargetBitmap);
            GlobalTools.SafeRelease(ref m_pDXGISwapChain1);

            GlobalTools.SafeRelease(ref m_pDXGIDevice);
            GlobalTools.SafeRelease(ref m_pD3D11DeviceContext);
            Marshal.Release(m_pD3D11DevicePtr);

            //   GlobalTools.SafeRelease(ref m_pWICImagingFactory);
            GlobalTools.SafeRelease(ref m_pD2DFactory1);
            GlobalTools.SafeRelease(ref m_pD2DFactory);

            GdiplusShutdown(m_initToken);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            Clean();
        }

        private int WindowSubClass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, uint dwRefData)
        {
            switch (uMsg)
            {
                case NativeHelpers.WM_ERASEBKGND:
                    {
                        RECT rect;
                        NativeHelpers.GetClientRect(hWnd, out rect);
                        //int nRet = ExcludeClipRect(wParam, 0, 0, rect.right, 35);
                        IntPtr hBrush = NativeHelpers.CreateSolidBrush(System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.Magenta));
                        //IntPtr hBrush = CreateSolidBrush((int)MakeArgb(255, 255, 0, 0));                       
                        //IntPtr hBrush = CreateSolidBrush(System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.FromArgb(255, 32, 32, 32)));
                        NativeHelpers.FillRect(wParam, ref rect, hBrush);
                        NativeHelpers.DeleteObject(hBrush);
                        return 1;
                    }
                    break;
            }
            return NativeHelpers.DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }
    }
}
