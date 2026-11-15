namespace Radar.Core;

/// <summary>ADR-003: definición de "estar en oferta".</summary>
public static class Oferta
{
    public static (bool enOferta, string criterio) Evaluar(double actual, double? regular, double? msrp)
    {
        if (regular is > 0 && regular > actual)
        {
            double desc = 1 - actual / regular.Value;
            return (desc >= 0.05, "regular_sitio");
        }
        if (regular is null && msrp is > 0)
            return (actual <= 0.85 * msrp.Value, "msrp");
        return (false, regular is null ? "sin_referencia" : "regular_sitio");
    }

    public static int PorcentajeDescuento(double actual, double? regular) =>
        regular is > 0 && regular > actual ? (int)Math.Round(100 * (1 - actual / regular.Value)) : 0;
}
