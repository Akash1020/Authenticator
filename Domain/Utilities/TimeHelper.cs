﻿using System;

namespace Domain.Utilities
{
    public class TimeHelper
    {
        private static TimeHelper instance;
        private static object syncRoot = new object();
        private TimeSpan difference;

        private const string NTP_SERVER = "0.pool.ntp.org";

        public static TimeHelper Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                        {
                            instance = new TimeHelper();
                        }
                    }
                }

                return instance;
            }
        }

        public DateTime DateTime
        {
            get
            {
                DateTime dateTime = DateTime.UtcNow;

                if (difference != null)
                {
                    dateTime = dateTime.Add(difference);
                }

                return dateTime;
            }
        }

        private TimeHelper()
        {

        }

        public async void InitializeTime()
        {
            //if (SettingsManager.Get<bool>(Setting.UseNTP))
            //{
            //    NtpClient client = new NtpClient();

            //    TimeSpan timeOut = SettingsManager.Get<TimeSpan>(Setting.NTPTimeout);

            //    if (timeOut.Seconds > 0)
            //    {
            //        client.Timeout = timeOut;
            //    }

            //    DateTime? dt = null;

            //    try
            //    {
            //        dt = await client.GetAsync(NTP_SERVER);
            //    }
            //    catch (Exception) { }

            //    DateTime local = DateTime.Now;

            //    if (dt != null)
            //    {
            //        int diffType = local.CompareTo((DateTime)dt);
            //        TimeSpan? diff;

            //        if (diffType > 0)
            //        {
            //            // NTP time is later
            //            diff = dt - local;
            //        }
            //        else
            //        {
            //            // Local time is later
            //            diff = local - dt;
            //        }

            //        if (difference != null)
            //        {
            //            difference = (TimeSpan)diff;
            //        }
            //    }
            //}
        }
    }
}
