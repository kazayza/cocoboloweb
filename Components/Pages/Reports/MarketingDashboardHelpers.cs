using COCOBOLOERPNEW.Services;
using Microsoft.AspNetCore.Components;

namespace COCOBOLOERPNEW.Components.Pages.Reports;

/// <summary>
/// Static helper methods for the Marketing Dashboard.
/// Moved here to avoid raw string literal parsing issues in .razor @code blocks.
/// </summary>
public static class MarketingDashboardHelpers
{
    /* ================================================================
       FUNNEL COLORS
       ================================================================ */

    public static readonly string[] FunnelColors =
    {
        "#1769d5",  // 1. إجمالي اللييدز — أزرق
        "#d84635",  // 2. المرفوض — أحمر
        "#7447c6",  // 3. المحول — بنفسجي
        "#efa21b",  // 4. فرص بيع — برتقالي
        "#15965e",  // 5. تم البيع — أخضر
        "#1594a8",
        "#eb7620"
    };

    /* ================================================================
       MONEY FORMATTING
       ================================================================ */

    public static string FmtMoney(decimal value)
    {
        return MarketingDashboardService.FmtMoney(value);
    }


    /* ================================================================
       KPI ICONS
       ================================================================ */

    public static MarkupString RenderKpiIcon(string key)
    {
        var svg = key switch
        {
            "spend" => """
                <svg viewBox="0 0 24 24">
                    <circle cx="8" cy="8" r="4.2"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.8"/>
                    <path d="M5 12.5L3.5 14.5L7 17"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                    <circle cx="15.5" cy="15.5" r="4.2"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.8"/>
                    <path d="M13.5 15.5H17.5"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"/>
                </svg>
                """,

            "leads" => """
                <svg viewBox="0 0 24 24">
                    <circle cx="12" cy="8" r="3.2"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.8"/>
                    <path d="M5.5 20C5.9 16 8 14 12 14C16 14 18.1 16 18.5 20"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"/>
                    <path d="M4 11V15M2 13H6"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.6"
                          stroke-linecap="round"/>
                </svg>
                """,

            "cpl" => """
                <svg viewBox="0 0 24 24">
                    <circle cx="12" cy="12" r="8.5"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.8"/>
                    <circle cx="12" cy="12" r="3"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.8"/>
                    <path d="M12 2V5M12 19V22"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"/>
                </svg>
                """,

            "qualified" => """
                <svg viewBox="0 0 24 24">
                    <path d="M12 3L14.8 8.4L20.8 9.3L16.4 13.5L17.4 19.5L12 16.7L6.6 19.5L7.6 13.5L3.2 9.3L9.2 8.4L12 3Z"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.7"
                          stroke-linejoin="round"/>
                    <path d="M9.5 12L11.2 13.7L14.8 10.2"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.7"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                </svg>
                """,

            "conv" => """
                <svg viewBox="0 0 24 24">
                    <path d="M7 7H17M17 7L14 4M17 7L14 10"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                </svg>
                """,

            "customers" => """
                <svg viewBox="0 0 24 24">
                    <circle cx="9" cy="8" r="3"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.8"/>
                    <circle cx="17" cy="10" r="2.3"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.6"/>
                    <path d="M3.5 20C3.9 16.2 5.8 14 9 14C12.2 14 14.1 16.2 14.5 20"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"/>
                    <path d="M15 15.5C18 15.6 19.8 17.2 20.2 20"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.6"
                          stroke-linecap="round"/>
                </svg>
                """,

            "cac" => """
                <svg viewBox="0 0 24 24">
                    <rect x="4" y="6" width="16" height="13" rx="2"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"/>
                    <path d="M8 6V4.5C8 3.7 8.7 3 9.5 3H14.5C15.3 3 16 3.7 16 4.5V6"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"/>
                    <path d="M12 10V15M9.5 12.5H14.5"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.7"
                          stroke-linecap="round"/>
                </svg>
                """,

            "revenue" => """
                <svg viewBox="0 0 24 24">
                    <path d="M4 19V5M4 19H21"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"/>
                    <path d="M7 15L11 11L14 13L20 7"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                    <path d="M17 7H20V10"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                </svg>
                """,

            "roas" => """
                <svg viewBox="0 0 24 24">
                    <path d="M4 18L8 14L11 16L19 8"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                    <path d="M15 8H19V12"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                    <path d="M5 5H13"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.7"
                          stroke-linecap="round"/>
                    <path d="M5 8H10"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.7"
                          stroke-linecap="round"/>
                </svg>
                """,

            _ => """
                <svg viewBox="0 0 24 24">
                    <circle cx="12" cy="12" r="8"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="1.8"/>
                    <path d="M12 8V12L15 14"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.8"
                          stroke-linecap="round"
                          stroke-linejoin="round"/>
                </svg>
                """
        };

        return new MarkupString(svg);
    }


    /* ================================================================
       SCORE GAUGE
       ================================================================ */

    public static string BuildGaugeStyle(decimal score)
    {
        var normalized =
            Math.Max(
                0,
                Math.Min(
                    10,
                    score
                )
            );

        var percentage =
            normalized * 10M;

        string color;

        if (normalized >= 9)
        {
            color = "#15965e";
        }
        else if (normalized >= 8)
        {
            color = "#35a86d";
        }
        else if (normalized >= 7)
        {
            color = "#91c63c";
        }
        else if (normalized >= 6)
        {
            color = "#efa21b";
        }
        else
        {
            color = "#d93445";
        }

        return
            $"background:conic-gradient({color} 0deg {percentage * 3.6M}deg, #e8edf3 {percentage * 3.6M}deg 360deg);";
    }
}
