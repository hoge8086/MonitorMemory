using System;
using System.Globalization;
using System.Windows.Data;

namespace MonitorMemory
{
    public class ByteConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double val = (double)((long)value);
            var units = new string[] {"byte", "KB", "MB", "GB", "TB"};
            string unit = "";

            foreach(var u in units)
            {
                if (val < 1024)
                {
                    unit = u;
                    break;
                }
                else
                    val /= 1024;
            }

            return $"{val:F2} {unit}";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
