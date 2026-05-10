using System.Globalization;
using System.Text;

namespace COCOBOLOERPNEW.Services
{
    public static class StringExtensions
    {
        public static string NormalizeArabic(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? "";

            var normalized = new StringBuilder(input.Length);
            
            foreach (char c in input)
            {
                switch (c)
                {
                    case 'أ': case 'إ': case 'آ':
                        normalized.Append('ا');
                        break;
                    case 'ة':
                        normalized.Append('ه');
                        break;
                    case 'ى':
                        normalized.Append('ي');
                        break;
                    case 'ئ': case 'ؤ':
                        normalized.Append('ء');
                        break;
                    default:
                        normalized.Append(c);
                        break;
                }
            }
            
            return normalized.ToString();
        }

        public static bool ContainsArabic(this string source, string searchTerm)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(searchTerm))
                return false;

            var normalizedSource = source.NormalizeArabic();
            var normalizedSearch = searchTerm.NormalizeArabic();

            return normalizedSource.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);
        }
    }
}