// =====================================================================
//  Développé de coupe de tube  ->  export DXF
//  Développé avec ♥ par Weapon · pour TolTem
//
//  - saisie : Ø, épaisseur, longueur, angle(s) de coupe
//  - recouvrement de soudure (couture longitudinale, après roulage)
//    = bande ajoutée dans le sens circonférentiel, sur toute la hauteur
//  - aperçu plan en direct + export DXF (contour fermé, mm)
// =====================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace DeveloppeTube;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* ignore si non supporté */ }
        Application.Run(new MainForm());
    }
}

// ---------------------------------------------------------------------
//  Modèle de données
// ---------------------------------------------------------------------
enum RefMode { Axe, Mini, Maxi }
enum CutMode { Single, Coude, Parallelogramme }
enum Seam { Talon, Cote, Gorge }

class Params
{
    public double D = 60, Th = 2, L = 150;
    public bool DevMoyen = false;
    public RefMode Ref = RefMode.Axe;
    public CutMode Cut = CutMode.Single;
    public double AngleA = 45, AngleB = 30;
    public Seam Seam = Seam.Talon;
    public bool OverlapOn = false;
    public double Overlap = 20;   // mm, sens circonférentiel (couture)

    public Params Clone() => (Params)MemberwiseClone();
}

// réglages persistés (dernière session + préréglages nommés)
class AppSettings
{
    public Params Last { get; set; } = new();
    public Dictionary<string, Params> Presets { get; set; } = new();
}

class Model
{
    public double Ddev, R, C, Uend, Overlap, SeamX;
    public double[] U = Array.Empty<double>();
    public double[] ZA = Array.Empty<double>();
    public double[] ZB = Array.Empty<double>();
    public double Lmax, Lmin, Ymax;
    public int IMax, IMin;
    public bool BotIsCut;
    public Params P = new();
}

// ---------------------------------------------------------------------
//  Géométrie du développé
// ---------------------------------------------------------------------
static class Geometry
{
    const int NBase = 360;

    public static Model Compute(Params p)
    {
        double D = Math.Max(0.1, p.D);
        double Ddev = p.DevMoyen ? Math.Max(0.1, D - p.Th) : D;
        double R = Ddev / 2.0;
        double C = Math.PI * Ddev;
        double L = Math.Max(0.1, p.L);
        double phi0 = p.Seam switch { Seam.Talon => 0, Seam.Cote => Math.PI / 2, _ => Math.PI };

        double aA = Clamp(p.AngleA, 0, 89) * Math.PI / 180.0;
        double aB = Clamp(p.AngleB, 0, 89) * Math.PI / 180.0;

        bool botIsCut = p.Cut != CutMode.Single;
        double AA = botIsCut ? R * Math.Tan(aA) : 0.0;
        double AB = (p.Cut == CutMode.Single) ? R * Math.Tan(aA) : R * Math.Tan(aB);

        // recouvrement = extension circonférentielle (le profil se prolonge périodiquement)
        double overlap = p.OverlapOn ? Math.Max(0, p.Overlap) : 0.0;
        double Uend = C + overlap;

        int M = Math.Max(8, (int)Math.Round(NBase * Uend / C));

        var U = new double[M + 1];
        var zAraw = new double[M + 1];
        var zBraw = new double[M + 1];

        for (int i = 0; i <= M; i++)
        {
            double u = Uend * i / M;
            double psi = 2 * Math.PI * u / C + phi0;
            U[i] = u;
            switch (p.Cut)
            {
                case CutMode.Single:
                    zAraw[i] = 0;             zBraw[i] = AB * Math.Cos(psi); break;
                case CutMode.Coude:
                    zAraw[i] = -AA * Math.Cos(psi); zBraw[i] = AB * Math.Cos(psi); break;
                default: // Parallelogramme
                    zAraw[i] = -AA * Math.Cos(psi); zBraw[i] = -AB * Math.Cos(psi); break;
            }
        }

        // g = longueur - Lc, sur la période de base [0,C]
        double gmax = double.NegativeInfinity, gmin = double.PositiveInfinity;
        for (int i = 0; i <= M; i++)
        {
            if (U[i] > C + 1e-9) continue;
            double g = zBraw[i] - zAraw[i];
            if (g > gmax) gmax = g;
            if (g < gmin) gmin = g;
        }
        double Lc = p.Ref switch { RefMode.Axe => L, RefMode.Maxi => L - gmax, _ => L - gmin };

        var ZA = new double[M + 1];
        var ZB = new double[M + 1];
        double minY = double.PositiveInfinity;
        for (int i = 0; i <= M; i++)
        {
            ZA[i] = zAraw[i];
            ZB[i] = Lc + zBraw[i];
            if (ZA[i] < minY) minY = ZA[i];
        }
        double off = -minY;
        double Lmax = double.NegativeInfinity, Lmin = double.PositiveInfinity, ymax = double.NegativeInfinity;
        int iMax = 0, iMin = 0;
        for (int i = 0; i <= M; i++)
        {
            ZA[i] += off; ZB[i] += off;
            if (ZB[i] > ymax) ymax = ZB[i];
            if (U[i] > C + 1e-9) continue;
            double ln = ZB[i] - ZA[i];
            if (ln > Lmax) { Lmax = ln; iMax = i; }
            if (ln < Lmin) { Lmin = ln; iMin = i; }
        }

        return new Model
        {
            Ddev = Ddev, R = R, C = C, Uend = Uend, Overlap = overlap, SeamX = C,
            U = U, ZA = ZA, ZB = ZB,
            Lmax = Lmax, Lmin = Lmin, Ymax = ymax,
            IMax = iMax, IMin = iMin, BotIsCut = botIsCut, P = p
        };
    }

    static double Clamp(double v, double a, double b) => v < a ? a : (v > b ? b : v);
}

// ---------------------------------------------------------------------
//  ArcFit : polyligne dense -> chaîne d'arcs TANGENTS (bulges DXF)
//  Pour le grignotage continu (AutoPunch / Metalix) : un arc = un passage
//  de poinçon continu, au lieu d'un coup par mini-segment.
//  Classe commune, à réutiliser telle quelle dans les autres outils TolTem.
// ---------------------------------------------------------------------
static class ArcFit
{
    public const double DefaultTol = 0.03;     // mm, écart max à la courbe d'origine
    const double MaxSweepHalf = 80.0 * Math.PI / 180.0; // |alpha| max par arc (balayage < 160°)

    /// <summary>Sommets (X, Y, Bulge) ; le bulge d'un sommet s'applique au segment vers le suivant.</summary>
    public static List<(double X, double Y, double B)> Fit(IReadOnlyList<(double X, double Y)> pts, double tol = DefaultTol)
    {
        var res = new List<(double X, double Y, double B)>();
        int n = pts.Count;
        if (n == 0) return res;
        if (n == 1) { res.Add((pts[0].X, pts[0].Y, 0)); return res; }

        // tangente de départ = direction du premier segment non nul
        double tx = 0, ty = 0;
        for (int k = 1; k < n; k++)
        {
            double ddx = pts[k].X - pts[0].X, ddy = pts[k].Y - pts[0].Y;
            double l = Math.Sqrt(ddx * ddx + ddy * ddy);
            if (l > 1e-9) { tx = ddx / l; ty = ddy / l; break; }
        }

        int i = 0;
        while (i < n - 1)
        {
            double x0 = pts[i].X, y0 = pts[i].Y;
            int best = -1; double bestAlpha = 0;

            for (int j = i + 1; j < n; j++)
            {
                double dx = pts[j].X - x0, dy = pts[j].Y - y0;
                double c = Math.Sqrt(dx * dx + dy * dy);
                if (c < 1e-9) { if (best < 0) continue; else break; }
                double alpha = Math.Atan2(tx * dy - ty * dx, tx * dx + ty * dy);
                if (Math.Abs(alpha) > MaxSweepHalf) break;

                bool ok = true;
                if (Math.Abs(alpha) < 1e-7)
                {
                    for (int k = i + 1; k < j && ok; k++)
                        if (Math.Abs(tx * (pts[k].Y - y0) - ty * (pts[k].X - x0)) > tol) ok = false;
                }
                else
                {
                    double r = c / (2 * Math.Sin(alpha));       // signé : >0 = arc vers la gauche
                    double cx = x0 - ty * r, cy = y0 + tx * r;
                    double ar = Math.Abs(r);
                    for (int k = i + 1; k < j && ok; k++)
                    {
                        double ex = pts[k].X - cx, ey = pts[k].Y - cy;
                        if (Math.Abs(Math.Sqrt(ex * ex + ey * ey) - ar) > tol) ok = false;
                    }
                }
                if (!ok) break;
                best = j; bestAlpha = alpha;
            }

            if (best < 0)
            {
                // angle vif (coin) : segment droit, la tangente repart du segment
                best = i + 1;
                while (best < n - 1 && Math.Abs(pts[best].X - x0) + Math.Abs(pts[best].Y - y0) < 1e-9) best++;
                double dx = pts[best].X - x0, dy = pts[best].Y - y0;
                double c = Math.Sqrt(dx * dx + dy * dy);
                res.Add((x0, y0, 0));
                if (c > 1e-9) { tx = dx / c; ty = dy / c; }
            }
            else
            {
                res.Add((x0, y0, Math.Tan(bestAlpha / 2)));
                double ca = Math.Cos(2 * bestAlpha), sa = Math.Sin(2 * bestAlpha);
                double ntx = tx * ca - ty * sa, nty = tx * sa + ty * ca;
                tx = ntx; ty = nty;
            }
            i = best;
        }
        res.Add((pts[n - 1].X, pts[n - 1].Y, 0));
        return res;
    }
}

// ---------------------------------------------------------------------
//  Export DXF (AC1015, millimètres, contour fermé)
// ---------------------------------------------------------------------
static class Dxf
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Build(Model m)
    {
        var sb = new StringBuilder();
        void P(int code, string val) { sb.Append(code).Append('\n').Append(val).Append('\n'); }
        void Pd(int code, double val) { P(code, val.ToString("0.####", Inv)); }

        // HEADER
        P(0, "SECTION"); P(2, "HEADER");
        P(9, "$ACADVER"); P(1, "AC1015");
        P(9, "$INSUNITS"); P(70, "4");          // 4 = millimètres
        P(0, "ENDSEC");

        // TABLES (calques)
        P(0, "SECTION"); P(2, "TABLES");
        P(0, "TABLE"); P(2, "LAYER"); P(70, "4");
        Layer(P, "DECOUPE", 7);       // blanc  = découpe (Metalix)
        Layer(P, "RECOUVREMENT", 3);  // vert   = recouvrement (axe de pointage)
        Layer(P, "TRACAGE", 6);       // violet = lignes de traçage
        Layer(P, "INFOS", 8);         // gris   = annotation
        P(0, "ENDTAB"); P(0, "ENDSEC");

        // ENTITIES
        P(0, "SECTION"); P(2, "ENTITIES");

        // contour fermé : bord A (gauche -> droite) puis bord B (droite -> gauche),
        // chaque bord converti en chaîne d'arcs tangents (bulges) = grignotage continu
        int n = m.U.Length;
        var ptsA = new List<(double X, double Y)>(n);
        var ptsB = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++) ptsA.Add((m.U[i], m.ZA[i]));
        for (int i = n - 1; i >= 0; i--) ptsB.Add((m.U[i], m.ZB[i]));

        var chain = ArcFit.Fit(ptsA);
        chain.AddRange(ArcFit.Fit(ptsB));

        P(0, "LWPOLYLINE"); P(8, "DECOUPE"); P(90, chain.Count.ToString(Inv)); P(70, "1"); // 1 = fermée
        foreach (var v in chain)
        {
            Pd(10, v.X); Pd(20, v.Y);
            if (Math.Abs(v.B) > 1e-9) P(42, v.B.ToString("0.########", Inv));
        }

        // repères (uniquement avec recouvrement)
        if (m.Overlap > 0)
        {
            double za0 = m.ZA[0], zb0 = m.ZB[0];
            // traçage (violet) : fin du tube nominal à x = circonférence
            Line(P, Pd, "TRACAGE", m.SeamX, za0, m.SeamX, zb0);
            // recouvrement (vert) : axe de la patte = ligne de pointage électropointé
            double xw = m.SeamX + m.Overlap / 2.0;
            int idx = Math.Clamp((int)Math.Round(xw / m.Uend * (m.U.Length - 1)), 0, m.U.Length - 1);
            Line(P, Pd, "RECOUVREMENT", xw, m.ZA[idx], xw, m.ZB[idx]);
        }

        // annotation (hors pièce)
        string dev = m.P.DevMoyen ? "dev-moy" : "dev-ext";
        string ang = m.P.Cut == CutMode.Single
            ? $"{m.P.AngleA:0.#}deg"
            : $"{m.P.AngleA:0.#}/{m.P.AngleB:0.#}deg";
        string ov = m.Overlap > 0 ? $" recouvr {m.Overlap:0.#}" : "";
        string txt = $"Tube O{m.P.D:0.#} {dev} L{m.P.L:0.#} {m.P.Ref} {ang}{ov}";
        P(0, "TEXT"); P(8, "INFOS"); Pd(10, 0); Pd(20, -12); P(40, "6"); P(1, txt);

        P(0, "ENDSEC"); P(0, "EOF");
        return sb.ToString();
    }

    static void Layer(Action<int, string> P, string name, int color)
    {
        P(0, "LAYER"); P(2, name); P(70, "0"); P(62, color.ToString(Inv)); P(6, "CONTINUOUS");
    }

    static void Line(Action<int, string> P, Action<int, double> Pd, string layer,
                     double x1, double y1, double x2, double y2)
    {
        P(0, "LINE"); P(8, layer); Pd(10, x1); Pd(20, y1); Pd(11, x2); Pd(21, y2);
    }
}

// ---------------------------------------------------------------------
//  Panneau de dessin (GDI+)
// ---------------------------------------------------------------------
class DrawPanel : Panel
{
    public Model? Model;

    public DrawPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(0x16, 0x1A, 0x20);
        ResizeRedraw = true;
    }

    static readonly Color Cut = Color.FromArgb(0xFF, 0x7A, 0x1A);
    static readonly Color Steel = Color.FromArgb(0x5B, 0x9D, 0xD9);
    static readonly Color SteelDim = Color.FromArgb(0x46, 0x72, 0x96);
    static readonly Color Grid = Color.FromArgb(0x22, 0x29, 0x32);
    static readonly Color Faint = Color.FromArgb(0x6D, 0x7B, 0x8A);
    static readonly Color Green = Color.FromArgb(0x46, 0xC9, 0x6B);
    static readonly Color Violet = Color.FromArgb(0xA8, 0x73, 0xE6);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        DrawGrid(g);

        var m = Model;
        if (m == null || m.U.Length < 2) return;

        float W = ClientSize.Width, H = ClientSize.Height;
        float ml = 78, mr = 46, mt = 44, mb = 64;
        float availW = Math.Max(10, W - ml - mr);
        float availH = Math.Max(10, H - mt - mb);
        float s = (float)Math.Min(availW / m.Uend, availH / m.Ymax);
        float ox = ml + (availW - (float)m.Uend * s) / 2f;
        float oyTop = mt + (availH - (float)m.Ymax * s) / 2f;

        float PX(double x) => ox + (float)x * s;
        float PY(double y) => oyTop + ((float)m.Ymax - (float)y) * s;

        int n = m.U.Length;

        // contour matière (avec recouvrement)
        var pts = new List<PointF>(2 * n);
        for (int i = 0; i < n; i++) pts.Add(new PointF(PX(m.U[i]), PY(m.ZA[i])));
        for (int i = n - 1; i >= 0; i--) pts.Add(new PointF(PX(m.U[i]), PY(m.ZB[i])));

        using (var fill = new SolidBrush(Color.FromArgb(16, Cut)))
            g.FillPolygon(fill, pts.ToArray());
        using (var pen = new Pen(Cut, 2.2f) { LineJoin = LineJoin.Round })
            g.DrawPolygon(pen, pts.ToArray());

        // repères de recouvrement (couleurs Metalix : violet = traçage, vert = pointage)
        if (m.Overlap > 0)
        {
            using (var pv = new Pen(Violet, 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawLine(pv, PX(m.SeamX), PY(m.ZA[0]), PX(m.SeamX), PY(m.ZB[0]));

            double xw = m.SeamX + m.Overlap / 2.0;
            int idx = Math.Clamp((int)Math.Round(xw / m.Uend * (m.U.Length - 1)), 0, m.U.Length - 1);
            using (var pg = new Pen(Green, 1.8f))
                g.DrawLine(pg, PX(xw), PY(m.ZA[idx]), PX(xw), PY(m.ZB[idx]));

            using var f = new Font("Consolas", 8f);
            using (var bv = new SolidBrush(Violet)) g.DrawString("fin tube", f, bv, PX(m.SeamX) - 48, PY(0) + 6);
            using (var bg = new SolidBrush(Green)) g.DrawString("pointage", f, bg, PX(xw) + 4, PY(0) + 6);
        }

        // cotes
        using (var dimPen = new Pen(SteelDim, 1f))
        using (var fdim = new Font("Consolas", 8.5f))
        using (var bdim = new SolidBrush(Steel))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            // circonférence (0 -> C)
            float yb = PY(0) + 30;
            g.DrawLine(dimPen, PX(0), yb, PX(m.C), yb);
            g.DrawLine(dimPen, PX(0), yb - 5, PX(0), yb + 5);
            g.DrawLine(dimPen, PX(m.C), yb - 5, PX(m.C), yb + 5);
            g.DrawString($"Ø dév · {m.C:0.0} mm", fdim, bdim, (PX(0) + PX(m.C)) / 2f, yb + 12, sf);

            // longueur maxi (gauche)
            VDim(g, dimPen, fdim, bdim, sf, PX(m.U[m.IMax]), PY(m.ZA[m.IMax]), PY(m.ZB[m.IMax]),
                 Math.Max(PX(0) - 34, 16), m.Lmax);
            // longueur mini (droite) si différente
            if (Math.Abs(m.Lmax - m.Lmin) > 0.05)
                VDim(g, dimPen, fdim, bdim, sf, PX(m.U[m.IMin]), PY(m.ZA[m.IMin]), PY(m.ZB[m.IMin]),
                     Math.Min(PX(m.Uend) + 34, W - 16), m.Lmin);
        }

        // vue de bout (coin haut-droit)
        DrawEndView(g, m, W);
    }

    void VDim(Graphics g, Pen pen, Font f, Brush b, StringFormat sf,
              float xAnchor, float y0, float y1, float xx, double val)
    {
        g.DrawLine(pen, xx, y0, xx, y1);
        g.DrawLine(pen, xx - 4, y0, xx + 4, y0);
        g.DrawLine(pen, xx - 4, y1, xx + 4, y1);
        using (var thin = new Pen(SteelDim, 0.7f) { DashStyle = DashStyle.Dot })
            g.DrawLine(thin, xAnchor, y1, xx, y1);
        var st = g.Save();
        g.TranslateTransform(xx - 10, (y0 + y1) / 2f);
        g.RotateTransform(-90);
        g.DrawString(val.ToString("0.0"), f, b, 0, 0, sf);
        g.Restore(st);
    }

    void DrawEndView(Graphics g, Model m, float W)
    {
        float cx = W - 58, cy = 50, r = 30;
        using var p1 = new Pen(Faint, 1.6f);
        using var p2 = new Pen(Color.FromArgb(0x33, 0x40, 0x4C), 1f);
        g.DrawEllipse(p1, cx - r, cy - r, 2 * r, 2 * r);
        float ri = Math.Max(2, r - 4);
        g.DrawEllipse(p2, cx - ri, cy - ri, 2 * ri, 2 * ri);
        double a = (m.P.Cut == CutMode.Single ? m.P.AngleA : m.P.AngleA) * Math.PI / 180.0;
        float t = (float)Math.Tan(a) * 0.45f;
        using var pc = new Pen(Cut, 2f);
        g.DrawLine(pc, cx - r, cy + r * t, cx + r, cy - r * t);
        using var f = new Font("Consolas", 7.5f);
        using var b = new SolidBrush(Faint);
        string lbl = m.P.Cut == CutMode.Single ? $"vue bout · {m.P.AngleA:0.#}°"
                                               : $"bout · {m.P.AngleA:0.#}/{m.P.AngleB:0.#}°";
        g.DrawString(lbl, f, b, cx - r - 6, cy + r + 4);
    }

    void DrawGrid(Graphics g)
    {
        using var pen = new Pen(Grid, 1f);
        const int step = 28;
        for (int x = 0; x < ClientSize.Width; x += step) g.DrawLine(pen, x, 0, x, ClientSize.Height);
        for (int y = 0; y < ClientSize.Height; y += step) g.DrawLine(pen, 0, y, ClientSize.Width, y);
    }
}

// ---------------------------------------------------------------------
//  Fenêtre principale
// ---------------------------------------------------------------------
class MainForm : Form
{
    readonly Params P = new();
    Model _model = new();

    DrawPanel _draw = null!;
    NumericUpDown _nD = null!, _nTh = null!, _nL = null!, _nA = null!, _nB = null!, _nOv = null!;
    ComboBox _cDev = null!, _cRef = null!, _cCut = null!, _cSeam = null!;
    CheckBox _chkOv = null!;
    Label _lblB = null!;
    Label _oC = null!, _oLmax = null!, _oLmin = null!, _oDdev = null!, _oW = null!, _oOv = null!;
    ComboBox _cPreset = null!;

    AppSettings _settings = new();
    bool _loading;
    static readonly JsonSerializerOptions JsonOpt = new()
    { IncludeFields = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    static readonly Color PanelBg = Color.FromArgb(0x1B, 0x20, 0x27);
    static readonly Color InputBg = Color.FromArgb(0x26, 0x2C, 0x35);
    static readonly Color Ink = Color.FromArgb(0xE6, 0xEB, 0xF0);
    static readonly Color Muted = Color.FromArgb(0x8A, 0x95, 0xA2);
    static readonly Color Accent = Color.FromArgb(0xFF, 0x7A, 0x1A);
    static readonly Color Bar = Color.FromArgb(0x20, 0x24, 0x2B);

    public MainForm()
    {
        // --- Base d'autoscale DPI ---
        // Form dessinee a 96 dpi (Segoe UI 9pt = 7x15). AutoScaleMode.Font scale ENSEMBLE
        // largeurs fixes + police vers 120/144 dpi (125%/150%) et l'inverse a 80%.
        // A poser AVANT toute creation de controle.
        AutoScaleDimensions = new SizeF(7f, 15f);
        AutoScaleMode = AutoScaleMode.Font;

        SuspendLayout();
        Text = "Développé de coupe de tube → DXF   ·   made with ♥ by Weapon for TolTem";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        Size = new Size(1140, 760);
        BackColor = PanelBg;
        Font = new Font("Segoe UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* pas d'icône en debug */ }

        BuildUi();
        LoadSettings();
        FormClosing += (_, _) => SaveSettings();
        ResumeLayout(false);
    }

    const int INNER = 262;

    void BuildUi()
    {
        // --- panneau gauche : contrôles (flow vertical + bouton) ---
        var left = new Panel { Dock = DockStyle.Left, Width = 320, BackColor = PanelBg, Padding = new Padding(16) };
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = PanelBg };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = PanelBg
        };

        AddTitle(flow, "TUBE");
        _nD = AddNum(flow, "Ø extérieur (mm)", (decimal)P.D, 1, 5000, 1, 0.5m, out _);
        _nTh = AddNum(flow, "Épaisseur (mm)", (decimal)P.Th, 0, 100, 2, 0.1m, out _);
        _cDev = AddCombo(flow, "Développer sur", new[] { "Ø extérieur (traçage)", "Ø moyen (fibre)" });

        AddTitle(flow, "LONGUEUR");
        _nL = AddNum(flow, "Longueur (mm)", (decimal)P.L, 0.1m, 100000, 1, 1m, out _);
        _cRef = AddCombo(flow, "Référence", new[] { "à l'axe (centre)", "mini (côté court)", "maxi (côté long)" });

        AddTitle(flow, "COUPES");
        _cCut = AddCombo(flow, "Type", new[] { "1 coupe (bout droit)", "2 coupes — coude", "2 coupes — parallélogramme" });
        _nA = AddNum(flow, "Angle 1 (°)", (decimal)P.AngleA, 0, 89, 1, 0.5m, out _);
        _nB = AddNum(flow, "Angle 2 (°)", (decimal)P.AngleB, 0, 89, 1, 0.5m, out _lblB);
        _cSeam = AddCombo(flow, "Couture", new[] { "Talon (long)", "Côté", "Gorge (court)" });

        AddTitle(flow, "RECOUVREMENT · ÉLECTROPOINTÉ");
        _chkOv = new CheckBox { Text = "Recouvrement soudure (roulage)", ForeColor = Ink, BackColor = PanelBg, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        flow.Controls.Add(_chkOv);
        _nOv = AddNum(flow, "Recouvrement (mm)", (decimal)P.Overlap, 0, 1000, 1, 1m, out _);

        AddTitle(flow, "PRÉRÉGLAGES");
        _cPreset = AddCombo(flow, "Préréglage enregistré", new[] { "—" });
        var pr = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            AutoSize = true, Margin = new Padding(0, 2, 0, 4), BackColor = PanelBg
        };
        var bLoad = SmallBtn("Charger");
        var bSave = SmallBtn("Enregistrer…");
        var bDel = SmallBtn("Suppr.");
        pr.Controls.Add(bLoad); pr.Controls.Add(bSave); pr.Controls.Add(bDel);
        flow.Controls.Add(pr);
        bLoad.Click += (_, _) =>
        {
            if (_cPreset.SelectedItem is string nm && _settings.Presets.TryGetValue(nm, out var pp))
                ApplyParams(pp);
        };
        bSave.Click += (_, _) =>
        {
            var cur = _cPreset.SelectedItem as string;
            var nm = PromptName("Enregistrer le préréglage", (cur != null && cur != "—") ? cur : "Tube");
            if (string.IsNullOrWhiteSpace(nm)) return;
            SyncParams();
            _settings.Presets[nm] = P.Clone();
            SaveSettings();
            RefreshPresetCombo();
            _cPreset.SelectedItem = nm;
        };
        bDel.Click += (_, _) =>
        {
            if (_cPreset.SelectedItem is string nm && _settings.Presets.Remove(nm))
            { SaveSettings(); RefreshPresetCombo(); }
        };

        var btn = new Button
        {
            Text = "⤓  Exporter le DXF", Dock = DockStyle.Fill,
            BackColor = Accent, ForeColor = Color.FromArgb(0x1A, 0x0C, 0x00),
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 0)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) => ExportDxf();

        split.Controls.Add(flow, 0, 0);
        split.Controls.Add(btn, 0, 1);
        left.Controls.Add(split);

        // --- bandeau bas : cotes ---
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Bar };
        var ro = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Bar };
        for (int i = 0; i < 6; i++) ro.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        _oC = AddReadout(ro, "Circonférence");
        _oLmax = AddReadout(ro, "Long. maxi");
        _oLmin = AddReadout(ro, "Long. mini");
        _oDdev = AddReadout(ro, "Ø développé");
        _oW = AddReadout(ro, "Largeur dév.");
        _oOv = AddReadout(ro, "Recouvrement");
        bottom.Controls.Add(ro);

        // --- zone de dessin ---
        _draw = new DrawPanel { Dock = DockStyle.Fill };

        Controls.Add(_draw);    // index 0 -> remplit le centre
        Controls.Add(bottom);   // bandeau bas
        Controls.Add(left);     // panneau gauche (pleine hauteur)

        // événements
        foreach (var n in new[] { _nD, _nTh, _nL, _nA, _nB, _nOv }) n.ValueChanged += (_, _) => Recompute();
        foreach (var c in new[] { _cDev, _cRef, _cCut, _cSeam }) c.SelectedIndexChanged += (_, _) => Recompute();
        _chkOv.CheckedChanged += (_, _) => Recompute();
    }

    // ---- helpers UI ----
    void AddTitle(FlowLayoutPanel f, string s)
    {
        f.Controls.Add(new Label
        {
            Text = s, ForeColor = Muted, AutoSize = true,
            Font = new Font("Consolas", 8f, FontStyle.Bold), Margin = new Padding(0, 12, 0, 2)
        });
    }

    NumericUpDown AddNum(FlowLayoutPanel f, string label, decimal val, decimal min, decimal max, int dec, decimal step, out Label lbl)
    {
        lbl = new Label { Text = label, ForeColor = Ink, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
        var n = new NumericUpDown
        {
            Minimum = min, Maximum = max, DecimalPlaces = dec, Increment = step, Value = Clamp(val, min, max),
            Width = INNER, TextAlign = HorizontalAlignment.Right, Font = new Font("Consolas", 9.5f),
            Margin = new Padding(0, 0, 0, 2), BorderStyle = BorderStyle.FixedSingle,
            BackColor = InputBg, ForeColor = Ink
        };
        f.Controls.Add(lbl); f.Controls.Add(n);
        return n;
    }

    ComboBox AddCombo(FlowLayoutPanel f, string label, string[] items)
    {
        f.Controls.Add(new Label { Text = label, ForeColor = Ink, AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        var c = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = INNER, Margin = new Padding(0, 0, 0, 2),
            FlatStyle = FlatStyle.Flat, BackColor = InputBg, ForeColor = Ink
        };
        c.Items.AddRange(items);
        c.SelectedIndex = 0;
        f.Controls.Add(c);
        return c;
    }

    Label AddReadout(TableLayoutPanel t, string key)
    {
        var cell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Bar, Padding = new Padding(10, 8, 6, 6) };
        cell.Controls.Add(new Label { Text = key.ToUpperInvariant(), ForeColor = Muted, AutoSize = true, Font = new Font("Consolas", 7.5f) });
        var v = new Label { Text = "–", ForeColor = Color.White, AutoSize = true, Font = new Font("Consolas", 12.5f) };
        cell.Controls.Add(v);
        t.Controls.Add(cell);
        return v;
    }

    static decimal Clamp(decimal v, decimal a, decimal b) => v < a ? a : (v > b ? b : v);

    Button SmallBtn(string text) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat,
        BackColor = InputBg, ForeColor = Ink, Font = new Font("Segoe UI", 8.5f),
        Margin = new Padding(0, 0, 6, 0), Padding = new Padding(4, 2, 4, 2)
    };

    // ---- pipeline ----
    void SyncParams()
    {
        P.D = (double)_nD.Value;
        P.Th = (double)_nTh.Value;
        P.L = (double)_nL.Value;
        P.AngleA = (double)_nA.Value;
        P.AngleB = (double)_nB.Value;
        P.DevMoyen = _cDev.SelectedIndex == 1;
        P.Ref = (RefMode)_cRef.SelectedIndex;
        P.Cut = (CutMode)_cCut.SelectedIndex;
        P.Seam = (Seam)_cSeam.SelectedIndex;
        P.OverlapOn = _chkOv.Checked;
        P.Overlap = (double)_nOv.Value;
    }

    void Recompute()
    {
        if (_loading) return;
        SyncParams();

        bool twoCuts = P.Cut != CutMode.Single;
        _nB.Enabled = twoCuts;
        _lblB.Text = twoCuts ? "Angle 2 (°)" : "Angle 2 (° — inutile)";
        _nOv.Enabled = _chkOv.Checked;

        _model = Geometry.Compute(P);
        _draw.Model = _model;
        _draw.Invalidate();

        _oC.Text = $"{_model.C:0.0}";
        _oLmax.Text = $"{_model.Lmax:0.0}";
        _oLmin.Text = $"{_model.Lmin:0.0}";
        _oDdev.Text = $"{_model.Ddev:0.0}";
        _oW.Text = $"{_model.Uend:0.0}";
        _oOv.Text = _model.Overlap > 0 ? $"{_model.Overlap:0.#}" : "—";
    }

    void ExportDxf()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Fichier DXF (*.dxf)|*.dxf",
            FileName = SuggestName()
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, Dxf.Build(_model), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Échec de l'écriture du fichier :\n" + ex.Message,
                "Export DXF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    string SuggestName()
    {
        string a = P.Cut == CutMode.Single ? $"{P.AngleA:0.#}" : $"{P.AngleA:0.#}-{P.AngleB:0.#}";
        string ov = P.OverlapOn && P.Overlap > 0 ? $"_rec{P.Overlap:0.#}" : "";
        return $"tube_D{P.D:0.#}_L{P.L:0.#}_{a}deg{ov}.dxf".Replace(" ", "");
    }

    // ---- persistance JSON ----
    static string SettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TolTem", "DeveloppeTube", "settings.json");

    void LoadSettings()
    {
        try
        {
            var path = SettingsPath();
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpt);
                if (s != null) _settings = s;
            }
        }
        catch { _settings = new AppSettings(); }

        RefreshPresetCombo();
        ApplyParams(_settings.Last);   // restaure la dernière session (et recalcule)
    }

    void SaveSettings()
    {
        try
        {
            SyncParams();
            _settings.Last = P.Clone();
            var path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(_settings, JsonOpt));
        }
        catch { /* sauvegarde silencieuse */ }
    }

    void RefreshPresetCombo()
    {
        _cPreset.Items.Clear();
        if (_settings.Presets.Count == 0)
            _cPreset.Items.Add("—");
        else
            foreach (var k in _settings.Presets.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                _cPreset.Items.Add(k);
        if (_cPreset.Items.Count > 0) _cPreset.SelectedIndex = 0;
    }

    void ApplyParams(Params p)
    {
        _loading = true;
        _nD.Value = Clamp((decimal)p.D, _nD.Minimum, _nD.Maximum);
        _nTh.Value = Clamp((decimal)p.Th, _nTh.Minimum, _nTh.Maximum);
        _nL.Value = Clamp((decimal)p.L, _nL.Minimum, _nL.Maximum);
        _nA.Value = Clamp((decimal)p.AngleA, _nA.Minimum, _nA.Maximum);
        _nB.Value = Clamp((decimal)p.AngleB, _nB.Minimum, _nB.Maximum);
        _nOv.Value = Clamp((decimal)p.Overlap, _nOv.Minimum, _nOv.Maximum);
        _cDev.SelectedIndex = p.DevMoyen ? 1 : 0;
        _cRef.SelectedIndex = Math.Clamp((int)p.Ref, 0, _cRef.Items.Count - 1);
        _cCut.SelectedIndex = Math.Clamp((int)p.Cut, 0, _cCut.Items.Count - 1);
        _cSeam.SelectedIndex = Math.Clamp((int)p.Seam, 0, _cSeam.Items.Count - 1);
        _chkOv.Checked = p.OverlapOn;
        _loading = false;
        Recompute();
    }

    // petit dialogue de saisie de nom (thème sombre)
    string? PromptName(string title, string def)
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(330, 110),
            MaximizeBox = false, MinimizeBox = false, BackColor = PanelBg, ForeColor = Ink,
            Font = new Font("Segoe UI", 9f),
            AutoScaleDimensions = new SizeF(7f, 15f), AutoScaleMode = AutoScaleMode.Font
        };
        var tb = new TextBox
        {
            Text = def, Left = 14, Top = 16, Width = 302,
            BackColor = InputBg, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle
        };
        var ok = new Button
        {
            Text = "OK", DialogResult = DialogResult.OK, Left = 156, Top = 60, Width = 76,
            FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.FromArgb(0x1A, 0x0C, 0x00)
        };
        var ca = new Button
        {
            Text = "Annuler", DialogResult = DialogResult.Cancel, Left = 240, Top = 60, Width = 76,
            FlatStyle = FlatStyle.Flat, BackColor = InputBg, ForeColor = Ink
        };
        f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(ca);
        f.AcceptButton = ok; f.CancelButton = ca;
        return f.ShowDialog(this) == DialogResult.OK ? tb.Text.Trim() : null;
    }
}
