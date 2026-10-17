using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickLook.Plugin.AiViewer
{
    /// <summary>
    /// The PDF-compatible part of an .ai file, rendered by our own copy of pdfium
    /// into a bitmap that keeps the page transparent (PDF viewers paint it white).
    /// </summary>
    public sealed class AiDocument : IDisposable
    {
        // Renamed copy of pdfium: QuickLook's PDFViewer loads its own "pdfium.dll" into the
        // same process, and a DllImport by that name could bind to (and share state with) it.
        private const string Dll = "pdfium_ai.dll";

        private const int FPDF_ANNOT = 0x01;

        // pdfium is not thread-safe: every call goes through this lock.
        private static readonly object Lock = new object();
        private static bool _libraryReady;

        private GCHandle _pin;
        private IntPtr _doc;

        public static string DllPath =>
            Path.Combine(Path.GetDirectoryName(typeof(AiDocument).Assembly.Location) ?? "", Dll);

        public int PageCount { get; }

        public AiDocument(string path)
        {
            // Load from memory so the file is not kept open (Illustrator may want to save it).
            var bytes = File.ReadAllBytes(path);

            lock (Lock)
            {
                EnsureLibrary();
                _pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                _doc = FPDF_LoadMemDocument(_pin.AddrOfPinnedObject(), bytes.Length, null);
                if (_doc == IntPtr.Zero)
                {
                    var err = FPDF_GetLastError();
                    _pin.Free();
                    throw new InvalidDataException($"pdfium could not open the file (error {err}).");
                }

                PageCount = FPDF_GetPageCount(_doc);
            }
        }

        /// <summary>Page size in PDF points (1/72 inch).</summary>
        public Size GetPageSize(int index)
        {
            lock (Lock)
            {
                ThrowIfDisposed();
                return FPDF_GetPageSizeByIndex(_doc, index, out var w, out var h) != 0
                    ? new Size(w, h)
                    : Size.Empty;
            }
        }

        /// <summary>Renders one page with a transparent background; the long side is maxSide pixels.</summary>
        public BitmapSource Render(int index, int maxSide)
        {
            lock (Lock)
            {
                ThrowIfDisposed();

                var page = FPDF_LoadPage(_doc, index);
                if (page == IntPtr.Zero)
                    throw new InvalidDataException($"pdfium could not load page {index + 1}.");

                try
                {
                    double w = FPDF_GetPageWidthF(page), h = FPDF_GetPageHeightF(page);
                    var scale = maxSide / Math.Max(1.0, Math.Max(w, h));
                    var pw = Math.Max(1, (int)Math.Round(w * scale));
                    var ph = Math.Max(1, (int)Math.Round(h * scale));

                    var bitmap = FPDFBitmap_Create(pw, ph, 1);
                    if (bitmap == IntPtr.Zero)
                        throw new OutOfMemoryException($"pdfium could not allocate a {pw}x{ph} bitmap.");

                    try
                    {
                        FPDFBitmap_FillRect(bitmap, 0, 0, pw, ph, 0x00000000); // fully transparent
                        FPDF_RenderPageBitmap(bitmap, page, 0, 0, pw, ph, 0, FPDF_ANNOT);

                        var stride = FPDFBitmap_GetStride(bitmap);
                        var source = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null,
                            FPDFBitmap_GetBuffer(bitmap), stride * ph, stride);
                        source.Freeze();
                        return source;
                    }
                    finally
                    {
                        FPDFBitmap_Destroy(bitmap);
                    }
                }
                finally
                {
                    FPDF_ClosePage(page);
                }
            }
        }

        public void Dispose()
        {
            lock (Lock)
            {
                if (_doc != IntPtr.Zero)
                {
                    FPDF_CloseDocument(_doc);
                    _doc = IntPtr.Zero;
                }

                if (_pin.IsAllocated)
                    _pin.Free();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_doc == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(AiDocument));
        }

        private static void EnsureLibrary()
        {
            if (_libraryReady)
                return;

            // Load by full path first, so the DllImports below bind to this exact module.
            if (LoadLibrary(DllPath) == IntPtr.Zero)
                throw new DllNotFoundException($"Could not load {DllPath} (Win32 error {Marshal.GetLastWin32Error()}).");

            FPDF_InitLibrary();
            _libraryReady = true;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport(Dll)] private static extern void FPDF_InitLibrary();
        [DllImport(Dll, CharSet = CharSet.Ansi)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string password);
        [DllImport(Dll)] private static extern uint FPDF_GetLastError();
        [DllImport(Dll)] private static extern int FPDF_GetPageCount(IntPtr doc);
        [DllImport(Dll)] private static extern int FPDF_GetPageSizeByIndex(IntPtr doc, int index, out double width, out double height);
        [DllImport(Dll)] private static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
        [DllImport(Dll)] private static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Dll)] private static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Dll)] private static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Dll)] private static extern void FPDF_CloseDocument(IntPtr doc);
        [DllImport(Dll)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
        [DllImport(Dll)] private static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Dll)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int w, int h, int rotate, int flags);
        [DllImport(Dll)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
        [DllImport(Dll)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
        [DllImport(Dll)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    }
}
