// =====================================================================
//  ArcFit : courbe -> chaîne d'arcs TANGENTS pour le grignotage continu
//  Repris tel quel de CoudeDev v1.4.1 (même moteur dans les deux outils).
//  Développé avec ♥ par Weapon · pour TolTem
// =====================================================================

using System;
using System.Collections.Generic;

namespace DeveloppeTube;

public struct V2 { public double X, Y; public V2(double x,double y){X=x;Y=y;}
    public static V2 operator -(V2 a, V2 b)=>new V2(a.X-b.X,a.Y-b.Y);
    public static V2 operator +(V2 a, V2 b)=>new V2(a.X+b.X,a.Y+b.Y);
    public static V2 operator *(V2 a, double s)=>new V2(a.X*s,a.Y*s);
    public double Norm()=>Math.Sqrt(X*X+Y*Y);
    public static double Dot(V2 a, V2 b)=>a.X*b.X+a.Y*b.Y;
    public static double Cross(V2 a, V2 b)=>a.X*b.Y-a.Y*b.X;
    public V2 Unit(){ double n=Norm(); return n<1e-300?new V2(0,0):new V2(X/n,Y/n); } }

/// <summary>
/// Sommet de LWPOLYLINE DXF : P = début du tronçon, Bulge = tan(balayage/4) du tronçon
/// P -> sommet suivant (0 = droite, &gt;0 = arc anti-horaire, &lt;0 = arc horaire).
/// </summary>
public struct BVertex { public V2 P; public double Bulge; public BVertex(V2 p, double b){P=p;Bulge=b;} }

// ---------------------------------------------------------------------
//  ArcFit : courbe -> chaîne d'arcs TANGENTS (G1).
//
//  FitLong (export DXF) : PEU d'arcs LONGS, AUCUN plus court que l'arc
//  mini. Programmation dynamique sur des nœuds pris sur la courbe exacte
//  (point + tangente exacte) : chaque saut nœud a -> nœud b est soit UN
//  arc (tangent aux deux bouts à KinkTolDeg près), soit un biarc (2 arcs
//  tangents entre eux). Un saut n'est accepté que si l'écart à la courbe
//  est ≤ tol ET si chaque arc fait ≥ arc mini. On garde le chemin qui a le
//  MOINS d'arcs, puis, à égalité, celui dont le plus court est le plus long.
//  => fini les dizaines de bouts de 10 mm que Metalix ne sait pas grignoter.
//
//  Fit (ancien, secours) : subdivision récursive en biarcs tant que
//  l'écart > tol. Ne sert plus que si FitLong échoue (jamais en pratique).
// ---------------------------------------------------------------------
public static class ArcFit
{
    const double MaxBulge = 0.41421356237;   // tan(22,5°) => arcs ≤ 90° (ancien Fit)
    const double MaxBulgeLong = 0.70020753821; // tan(35°) => arcs ≤ 140° (FitLong)
    public const double KinkTolDeg = 0.1;    // cassure tolérée à un nœud pour un arc simple

    /// <summary>Balayage MINI d'un arc écrit au DXF (degrés). En dessous, l'arc est "quasi plat"
    /// (point d'inflexion de l'onglet : rayon de plusieurs centaines de mètres) et il est écrit
    /// en DROITE. cncKad / Metalix v24 lit un tel arc comme un CERCLE COMPLET de ce rayon :
    /// Ø160 à 135° en 2 éléments, P1 femelle -> arc de 0,008° (R 390 m), aperçu cncKad
    /// = rond de 781 450 mm. Une droite à la place ajoute une cassure ≤ balayage/2 (≤ 0,05°).</summary>
    public const double MinSweepDeg = 0.1;
    /// <summary>Flèche maxi (mm) d'un arc quasi plat remplacé par UNE droite ; au-delà (arc plat
    /// mais très long, ex. bout de cône quasi cylindrique) il est remplacé par plusieurs droites.</summary>
    public const double FlatMaxFleche = 0.02;
    static readonly double FlatBulge = Math.Tan(MinSweepDeg * Math.PI / 180.0 / 4.0);

    /// <summary>true si le bulge est non nul mais l'arc trop plat pour la FAO (balayage &lt; MinSweepDeg).</summary>
    public static bool IsFlat(double bulge) => bulge != 0 && Math.Abs(bulge) < FlatBulge;

    /// <summary>Remplace les arcs quasi plats par des droites (1 seule si flèche ≤ FlatMaxFleche,
    /// sinon N cordes posées sur l'arc, jamais plus courtes que l'arc mini).</summary>
    public static int Flatten(List<BVertex> v, V2 end, double minLen)
    {
        int count = 0;
        for (int k = v.Count - 1; k >= 0; k--)
        {
            double bu = v[k].Bulge;
            if (!IsFlat(bu)) continue;
            count++;
            V2 a = v[k].P, b = (k + 1 < v.Count) ? v[k + 1].P : end;
            double c = (b - a).Norm();
            double fl = Math.Abs(bu) * c / 2;                                  // flèche de l'arc
            int ns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(fl / FlatMaxFleche)));
            if (minLen > 0) ns = Math.Min(ns, Math.Max(1, (int)Math.Floor(c / minLen)));
            if (ns == 1) { v[k] = new BVertex(a, 0); continue; }
            Circle(a, b, bu, out V2 C, out double R);
            double a0 = Math.Atan2(a.Y - C.Y, a.X - C.X), sw = 4 * Math.Atan(bu);
            var rep = new List<BVertex>(ns) { new BVertex(a, 0) };
            for (int j = 1; j < ns; j++)
            {
                double an = a0 + sw * j / ns;
                rep.Add(new BVertex(new V2(C.X + R * Math.Cos(an), C.Y + R * Math.Sin(an)), 0));
            }
            v.RemoveAt(k);
            v.InsertRange(k, rep);
        }
        return count;
    }

    public struct Stats
    {
        public double MinLen;    // plus court arc produit (mm)
        public double MaxDev;    // écart maxi mesuré sur TOUS les échantillons (mm)
        public double UsedMin;   // arc mini réellement tenu (abaissé seulement si impossible)
        public double UsedTol;   // tolérance réellement utilisée (montée auto pour tenir l'arc mini)
        public int Arcs;
        public int Flats;        // arcs quasi plats écrits en droite (balayage < MinSweepDeg)
    }

    // Arc (ou droite) prêt pour les mesures d'écart : centre, rayon, sens.
    readonly struct ArcGeo
    {
        readonly V2 A, B, C; readonly double R; readonly int S; readonly bool Line;
        public readonly double Len;
        public ArcGeo(V2 a, V2 b, double bulge)
        {
            A = a; B = b;
            double c = (b - a).Norm();
            if (Math.Abs(bulge) < 1e-9) { Line = true; C = a; R = 0; S = 0; Len = c; return; }
            Line = false;
            Circle(a, b, bulge, out C, out R);
            S = bulge > 0 ? 1 : -1;
            double sw = Math.Abs(4 * Math.Atan(bulge));
            Len = R * sw;
        }
        // Distance d'un point à l'arc (secteur testé par produits vectoriels : arcs < 180°).
        public double Dist(V2 q)
        {
            if (Line) return DistSeg(q, A, B);
            V2 qa = A - C, qq = q - C, qb = B - C;
            if (S * V2.Cross(qa, qq) >= 0 && S * V2.Cross(qq, qb) >= 0)
                return Math.Abs(qq.Norm() - R);
            return Math.Min((q - A).Norm(), (q - B).Norm());
        }
    }

    /// <summary>
    /// Courbe dense (P, T unitaires, pas ~0,5 mm) -> sommets à bulge du premier point à
    /// l'avant-dernier tronçon (le point final est porté par le sommet suivant du contour).
    /// Priorité à l'arc mini (= Ø du rond : en dessous Metalix saute l'arc) :
    ///   1) on essaie à tol ; si l'arc mini ne tient pas, la tolérance monte par paliers
    ///      (×1,25) jusqu'à tolMax ;
    ///   2) si même à tolMax c'est impossible (petit Ø très cintré), on garde tolMax et on
    ///      cherche le plus grand arc mini tenable (dichotomie). Valeurs rendues dans st.
    /// </summary>
    public static List<BVertex> FitLong(V2[] P, V2[] T, double tol, double tolMax, double minLen, out Stats st)
    {
        tol = Math.Max(0.005, tol);
        tolMax = Math.Max(tol, tolMax);
        minLen = Math.Max(0, minLen);
        int n = P.Length - 1;
        var res = new List<BVertex>();
        st = new Stats { MinLen = double.PositiveInfinity, UsedMin = minLen, UsedTol = tol };
        if (n < 1) return res;

        // longueur cumulée le long de la courbe
        var cum = new double[n + 1];
        for (int i = 1; i <= n; i++) cum[i] = cum[i - 1] + (P[i] - P[i - 1]).Norm();
        double len = cum[n];

        // nœuds : ~1 tous les 2 mm (≤ 300 nœuds) ; contrôle d'écart ~ tous les 1 mm
        double nodeStep = Math.Max(2.0, len / 300.0);
        int m = Math.Max(1, (int)Math.Ceiling(len / nodeStep));
        var idx = new int[m + 1];
        for (int k = 0, i = 0; k <= m; k++)
        {
            double target = len * k / m;
            while (i < n && cum[i] < target) i++;
            idx[k] = (k == 0) ? 0 : (k == m) ? n : Math.Max(idx[k - 1] + 1, Math.Min(n - (m - k), i));
        }
        double h = len / n;
        int stride = Math.Max(1, (int)Math.Round(nodeStep / 2 / Math.Max(1e-9, h)));

        // 1) arc mini tenu : tolérance montée par paliers si besoin
        double used = minLen, usedTol = tol;
        List<BVertex>? path = null;
        for (double t = tol; ; t = Math.Min(tolMax, t * 1.25))
        {
            path = Dp(P, T, cum, idx, stride, t, minLen);
            usedTol = t;
            if (path != null || t >= tolMax - 1e-12) break;
        }

        // 2) impossible même à tolMax : plus grand arc mini tenable (la faisabilité ne fait que
        //    croître quand l'arc mini baisse -> dichotomie, ~0,3 mm près)
        if (path == null)
        {
            double lo = 0, hi = minLen;
            List<BVertex>? best = Dp(P, T, cum, idx, stride, usedTol, 0);
            for (int it = 0; it < 7 && best != null; it++)
            {
                double mid = (lo + hi) / 2;
                var trial = Dp(P, T, cum, idx, stride, usedTol, mid);
                if (trial != null) { lo = mid; best = trial; } else hi = mid;
            }
            path = best; used = lo;
        }
        if (path == null)
        {   // secours (ne devrait jamais servir) : ancien découpage récursif
            path = new List<BVertex>();
            Fit(P, T, usedTol, path);
            used = 0;
        }
        Merge(path, P[n]);
        // zones quasi plates (inflexion de l'onglet) : droite au lieu d'un arc de R énorme
        st.Flats = Flatten(path, P[n], used);

        // contrôle final sur TOUS les échantillons : écart réel + plus court arc
        st.UsedMin = used;
        st.UsedTol = usedTol;
        st.Arcs = path.Count;
        int seg = 0;
        double maxDev = 0, minArc = double.PositiveInfinity;
        var geos = new ArcGeo[path.Count];
        for (int k = 0; k < path.Count; k++)
        {
            V2 b = (k + 1 < path.Count) ? path[k + 1].P : P[n];
            geos[k] = new ArcGeo(path[k].P, b, path[k].Bulge);
            minArc = Math.Min(minArc, geos[k].Len);
        }
        for (int i = 0; i <= n; i++)
        {
            double d = double.PositiveInfinity;
            // le point i est près du tronçon courant ou des voisins : fenêtre glissante
            for (int k = Math.Max(0, seg - 1); k < Math.Min(path.Count, seg + 3); k++)
            {
                double dk = geos[k].Dist(P[i]);
                if (dk < d) { d = dk; if (k > seg) seg = k; }
            }
            if (d > maxDev) maxDev = d;
        }
        st.MaxDev = maxDev;
        st.MinLen = minArc;
        return path;
    }

    // Programmation dynamique : moins d'arcs d'abord, puis plus long "plus court arc".
    static List<BVertex>? Dp(V2[] P, V2[] T, double[] cum, int[] idx, int stride, double tol, double minLen)
    {
        int m = idx.Length - 1;
        var cnt = new int[m + 1]; var bot = new double[m + 1]; var from = new int[m + 1];
        var e1 = new BVertex[m + 1]; var e2 = new BVertex[m + 1]; var two = new bool[m + 1];
        for (int k = 0; k <= m; k++) { cnt[k] = int.MaxValue; bot[k] = 0; from[k] = -1; }
        cnt[0] = 0; bot[0] = double.PositiveInfinity;

        double kinkTol = KinkTolDeg * Math.PI / 180.0;
        double reachLim = 3 * tol;                        // au-delà, plus aucun saut ne tiendra
        double[] ratios = { 1.0, 0.75, 1.0 / 0.75, 0.55, 1.0 / 0.55, 0.4, 1.0 / 0.4, 0.28, 1.0 / 0.28, 0.2, 5.0 };

        // écart du biarc "d égaux" a -> b (infini si impossible) : mesure la portée depuis a
        double Reach(int ia, int ib, double lim)
        {
            if (!Biarc(P[ia], T[ia], P[ib], T[ib], 1.0, out V2 J, out double c1, out double c2)
                || Math.Abs(c1) > MaxBulgeLong || Math.Abs(c2) > MaxBulgeLong) return double.PositiveInfinity;
            return Err2(P, ia, ib, stride, new ArcGeo(P[ia], J, c1), new ArcGeo(J, P[ib], c2), lim);
        }

        for (int a = 0; a < m; a++)
        {
            if (cnt[a] == int.MaxValue) continue;
            if (cnt[m] != int.MaxValue && cnt[a] + 1 > cnt[m]) continue;   // ne peut plus battre le meilleur
            int ia = idx[a];

            // 1) portée maxi depuis a (recherche exponentielle puis dichotomie)
            int good = a, badB = -1, step = 1;
            while (true)
            {
                int b = Math.Min(m, a + step);
                if (Reach(ia, idx[b], reachLim) <= reachLim) { good = b; if (b == m) break; step *= 2; }
                else { badB = b; break; }
            }
            if (badB > 0)
                while (badB - good > 1)
                {
                    int mid = (good + badB) / 2;
                    if (Reach(ia, idx[mid], reachLim) <= reachLim) good = mid; else badB = mid;
                }
            int bMax = Math.Max(a + 1, good);

            // 2) sauts candidats a -> b (seulement là où ça peut améliorer le nœud b)
            for (int b = a + 1; b <= bMax; b++)
            {
                int ib = idx[b];
                double span = cum[ib] - cum[ia];
                if (span < minLen - 1e-9) continue;                        // même un arc seul serait trop court

                bool canOne = cnt[a] + 1 < cnt[b] || (cnt[a] + 1 == cnt[b] && Math.Min(bot[a], span * 1.02) > bot[b] + 1e-9);
                bool canTwo = span >= 2 * minLen - 1e-9 &&
                              (cnt[a] + 2 < cnt[b] || (cnt[a] + 2 == cnt[b] && Math.Min(bot[a], span * 0.51) > bot[b] + 1e-9));
                if (!canOne && !canTwo) continue;

                // --- 1 seul arc, tangent au départ, qui retombe sur la tangente d'arrivée ---
                if (canOne)
                {
                    double bs = BulgeFromTangent(P[ia], T[ia], P[ib]);
                    if (Math.Abs(bs) <= MaxBulgeLong)
                    {
                        V2 et = EndTangent(P[ia], P[ib], bs);
                        double kink = Math.Abs(Math.Atan2(V2.Cross(et, T[ib]), V2.Dot(et, T[ib])));
                        if (kink <= kinkTol)
                        {
                            var g = new ArcGeo(P[ia], P[ib], bs);
                            if (g.Len >= minLen && Err1(P, ia, ib, stride, g, tol) <= tol)
                            {
                                double nb = Math.Min(bot[a], g.Len);
                                if (cnt[a] + 1 < cnt[b] || (cnt[a] + 1 == cnt[b] && nb > bot[b] + 1e-9))
                                { cnt[b] = cnt[a] + 1; bot[b] = nb; from[b] = a; two[b] = false; e1[b] = new BVertex(P[ia], bs); }
                                continue;   // un arc seul bat toujours un biarc sur le même saut
                            }
                        }
                    }
                }

                // --- biarc : plusieurs points de jonction essayés pour équilibrer les 2 arcs ---
                if (!canTwo) continue;
                double bestBot = -1; BVertex bv1 = default, bv2 = default;
                foreach (double r in ratios)
                {
                    if (!Biarc(P[ia], T[ia], P[ib], T[ib], r, out V2 J, out double c1, out double c2)
                        || Math.Abs(c1) > MaxBulgeLong || Math.Abs(c2) > MaxBulgeLong) continue;
                    var g1 = new ArcGeo(P[ia], J, c1); var g2 = new ArcGeo(J, P[ib], c2);
                    double lo = Math.Min(g1.Len, g2.Len);
                    if (lo < minLen || lo <= bestBot) continue;
                    if (Err2(P, ia, ib, stride, g1, g2, tol) > tol) continue;
                    bestBot = lo; bv1 = new BVertex(P[ia], c1); bv2 = new BVertex(J, c2);
                }
                if (bestBot >= 0)
                {
                    double nb = Math.Min(bot[a], bestBot);
                    if (cnt[a] + 2 < cnt[b] || (cnt[a] + 2 == cnt[b] && nb > bot[b] + 1e-9))
                    { cnt[b] = cnt[a] + 2; bot[b] = nb; from[b] = a; two[b] = true; e1[b] = bv1; e2[b] = bv2; }
                }
            }
        }
        if (cnt[m] == int.MaxValue) return null;

        var rev = new List<BVertex>();
        for (int k = m; k > 0; k = from[k])
        {
            if (two[k]) { rev.Add(e2[k]); rev.Add(e1[k]); }
            else rev.Add(e1[k]);
        }
        rev.Reverse();
        return rev;
    }

    // Écart maxi des échantillons (pas = stride) à 1 arc ; sort dès qu'on dépasse lim.
    static double Err1(V2[] P, int i0, int i1, int stride, in ArcGeo g, double lim)
    {
        double mx = 0;
        for (int i = i0 + stride; i < i1; i += stride)
        {
            double d = g.Dist(P[i]);
            if (d > mx) { mx = d; if (mx > lim) return mx; }
        }
        if (i1 - 1 > i0) mx = Math.Max(mx, g.Dist(P[i1 - 1]));
        return mx;
    }

    // Idem pour un biarc (distance = la plus petite aux 2 arcs).
    static double Err2(V2[] P, int i0, int i1, int stride, in ArcGeo g1, in ArcGeo g2, double lim)
    {
        double mx = 0;
        for (int i = i0 + stride; i < i1; i += stride)
        {
            double d = Math.Min(g1.Dist(P[i]), g2.Dist(P[i]));
            if (d > mx) { mx = d; if (mx > lim) return mx; }
        }
        if (i1 - 1 > i0) mx = Math.Max(mx, Math.Min(g1.Dist(P[i1 - 1]), g2.Dist(P[i1 - 1])));
        return mx;
    }

    public static void Fit(V2[] P, V2[] T, double tol, List<BVertex> outV)
    {
        if (P.Length < 2) return;
        Rec(P, T, 0, P.Length - 1, Math.Max(1e-4, tol), outV);
    }

    static void Rec(V2[] P, V2[] T, int i0, int i1, double tol, List<BVertex> outV)
    {
        if (Biarc(P[i0], T[i0], P[i1], T[i1], out V2 J, out double b1, out double b2)
            && Math.Abs(b1) <= MaxBulge && Math.Abs(b2) <= MaxBulge
            && (i1 - i0 <= 1 || MaxErr(P, i0, i1, J, b1, b2) <= tol))
        {
            outV.Add(new BVertex(P[i0], b1));
            outV.Add(new BVertex(J, b2));
            return;
        }
        if (i1 - i0 <= 1) { outV.Add(new BVertex(P[i0], 0)); return; }
        int im = (i0 + i1) / 2;
        Rec(P, T, i0, im, tol, outV);
        Rec(P, T, im, i1, tol, outV);
    }

    // Biarc "d égaux" : P0 (tangente T0) -> J -> P1 (tangente T1), tangent en J.
    static bool Biarc(V2 p0, V2 t0, V2 p1, V2 t1, out V2 J, out double b1, out double b2)
        => Biarc(p0, t0, p1, t1, 1.0, out J, out b1, out b2);

    // Biarc général : d1 = r·d0 (r = 1 -> "d égaux"). r déplace le point de jonction J
    // le long du bord : sert à équilibrer les 2 arcs pour qu'aucun ne soit trop court.
    static bool Biarc(V2 p0, V2 t0, V2 p1, V2 t1, double r, out V2 J, out double b1, out double b2)
    {
        J = p0; b1 = b2 = 0;
        V2 v = p1 - p0, w = t0 + t1 * r;
        double vv = V2.Dot(v, v), vw = V2.Dot(v, w), a = V2.Dot(w, w) - (1 + r) * (1 + r);   // a ≤ 0
        if (vv < 1e-18) return false;
        double disc = vw * vw - a * vv; if (disc < 0) return false;
        double den = vw + Math.Sqrt(disc);
        if (den < 1e-12) return false;
        double d = vv / den;                       // racine stable de a·d² - 2·vw·d + vv = 0
        if (!(d > 0) || double.IsInfinity(d)) return false;
        V2 q0 = p0 + t0 * d, q1 = p1 - t1 * (r * d);
        J = q0 + (q1 - q0) * (1.0 / (1 + r));
        V2 tJ = (q1 - q0).Unit();
        b1 = BulgeFromTangent(p0, t0, J);
        b2 = BulgeFromTangent(J, tJ, p1);
        return !double.IsNaN(b1) && !double.IsNaN(b2);
    }

    static double BulgeFromTangent(V2 a, V2 t, V2 b)
    {
        V2 c = b - a;
        double g = Math.Atan2(V2.Cross(t, c), V2.Dot(t, c));   // demi-balayage
        double bu = Math.Tan(g / 2);
        return Math.Abs(bu) < 1e-12 ? 0 : bu;
    }

    static double MaxErr(V2[] P, int i0, int i1, V2 J, double b1, double b2)
    {
        double mx = 0;
        for (int i = i0 + 1; i < i1; i++)
        {
            double d = Math.Min(DistArc(P[i], P[i0], J, b1), DistArc(P[i], J, P[i1], b2));
            if (d > mx) mx = d;
        }
        return mx;
    }

    public static void Circle(V2 a, V2 b, double bulge, out V2 C, out double R)
    {
        V2 c = b - a, mid = (a + b) * 0.5, perp = new V2(-c.Y, c.X);
        C = mid + perp * ((1 - bulge * bulge) / (4 * bulge));
        R = (a - C).Norm();
    }

    public static V2 StartTangent(V2 a, V2 b, double bulge)
    {
        V2 c = (b - a).Unit(); double g = 2 * Math.Atan(bulge);       // demi-balayage
        double cg = Math.Cos(-g), sg = Math.Sin(-g);
        return new V2(c.X * cg - c.Y * sg, c.X * sg + c.Y * cg);
    }
    public static V2 EndTangent(V2 a, V2 b, double bulge)
    {
        V2 c = (b - a).Unit(); double g = 2 * Math.Atan(bulge);
        double cg = Math.Cos(g), sg = Math.Sin(g);
        return new V2(c.X * cg - c.Y * sg, c.X * sg + c.Y * cg);
    }

    static double DistSeg(V2 q, V2 a, V2 b)
    {
        V2 ab = b - a; double l2 = V2.Dot(ab, ab);
        double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
        return (q - (a + ab * t)).Norm();
    }

    static double DistArc(V2 q, V2 a, V2 b, double bulge)
    {
        if (Math.Abs(bulge) < 1e-9) return DistSeg(q, a, b);
        Circle(a, b, bulge, out V2 C, out double R);
        double sw = 4 * Math.Atan(bulge);
        double rel = Math.Atan2(q.Y - C.Y, q.X - C.X) - Math.Atan2(a.Y - C.Y, a.X - C.X);
        const double TP = 2 * Math.PI;
        bool inside;
        if (sw > 0) { rel = ((rel % TP) + TP) % TP; inside = rel <= sw; }
        else        { rel = -((((-rel) % TP) + TP) % TP); inside = rel >= sw; }
        if (inside) return Math.Abs((q - C).Norm() - R);
        return Math.Min((q - a).Norm(), (q - b).Norm());
    }

    /// <summary>Fusionne les tronçons consécutifs sur le même cercle (ou la même droite).</summary>
    public static void Merge(List<BVertex> v, V2 end)
    {
        int i = 0;
        while (i + 1 < v.Count)
        {
            V2 a = v[i].P, m = v[i + 1].P, c = (i + 2 < v.Count) ? v[i + 2].P : end;
            if (CanMerge(a, v[i].Bulge, m, v[i + 1].Bulge, c, out double nb))
            { v[i] = new BVertex(a, nb); v.RemoveAt(i + 1); }
            else i++;
        }
    }

    static bool CanMerge(V2 a, double b1, V2 m, double b2, V2 c, out double nb)
    {
        nb = 0;
        bool s1 = b1 == 0, s2 = b2 == 0;
        if (s1 && s2)
        {
            V2 d1 = m - a, d2 = c - m; double l = d1.Norm() * d2.Norm();
            return l > 0 && Math.Abs(V2.Cross(d1, d2)) <= 1e-9 * l && V2.Dot(d1, d2) > 0;
        }
        if (s1 || s2 || Math.Sign(b1) != Math.Sign(b2)) return false;
        Circle(a, m, b1, out V2 C1, out double R1);
        Circle(m, c, b2, out V2 C2, out double R2);
        double tc = 1e-7 * Math.Max(1, R1);
        if ((C1 - C2).Norm() > tc || Math.Abs(R1 - R2) > tc) return false;
        double sw = 4 * Math.Atan(b1) + 4 * Math.Atan(b2);
        if (Math.Abs(sw) > Math.PI * 0.999) return false;   // on garde des arcs < 180°
        nb = Math.Tan(sw / 4);
        return true;
    }
}
