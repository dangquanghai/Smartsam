using Google.Cloud.Vision.V1;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SmartSam.Services
{
   
    public class GeneralServices 
    {
        public static (int Month, int Year) GetDefaultMonthYear(int boundaryDays = 5)
        {
            var now = DateTime.Now;
            int month = now.Month;
            int year = now.Year;

           // int daysInMonth = DateTime.DaysInMonth(year, month);

            if (now.Day <= boundaryDays )
            {
                month--;
                if (month == 0)
                {
                    month = 12;
                    year--;
                }
            }
            //year = 2025;
            //month = 12;
            return (month, year);
        }

        public static object GetDbValue(object value)
        {
            return value == null ? (object)DBNull.Value : value;
        }

        public static object GetStringDbValue(string value)
        {
            return string.IsNullOrEmpty(value) ? (object)DBNull.Value : value;
        }
    }
   
}
