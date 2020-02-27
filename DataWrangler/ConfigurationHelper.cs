﻿using System.Collections.Generic;
using System.Configuration;
using System.Reflection;

namespace DataWrangler
{
    public class ConfigurationHelper
    {
        public static bool SaveDbSettings(string dbFilePath, bool isEncrypted = false, string dbPass = null)
        {
            if (string.IsNullOrEmpty(dbFilePath)) return false;

            var configuration = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location);

            configuration.AppSettings.Settings.Remove("dbFilePath");
            configuration.AppSettings.Settings.Remove("dbPass");

            if (configuration.AppSettings.Settings["dbFilePath"] != null)
                configuration.AppSettings.Settings["dbFilePath"].Value = dbFilePath;
            else
                configuration.AppSettings.Settings.Add("dbFilePath", dbFilePath);

            if (isEncrypted && !string.IsNullOrEmpty(dbPass))
            {
                if (configuration.AppSettings.Settings["dbPass"] != null)
                    configuration.AppSettings.Settings["dbPass"].Value = dbPass;
                else
                    configuration.AppSettings.Settings.Add("dbPass", dbPass);
            }

            configuration.Save();
            ConfigurationManager.RefreshSection("appSettings");
            return true;
        }

        public static Dictionary<string, string> GetDbSettings()
        {
            var settings = new Dictionary<string, string>();

            var keys = new[] {"dbFilePath", "dbPass"};
            foreach (var key in keys)
            {
                var keyValue = ConfigurationManager.AppSettings[key];
                if (!string.IsNullOrEmpty(keyValue)) settings.Add(key, keyValue);
            }

            return settings;
        }

        public static string GetConnectionString()
        {
            var dbSettings = GetDbSettings();
            return GetConnectionString(dbSettings);
        }

        public static string GetConnectionString(Dictionary<string, string> dbSettings)
        {
            string connectionString;
            if (!dbSettings.ContainsKey("dbPass"))
                connectionString = string.Format("Filename={0};Connection=shared", dbSettings["dbFilePath"]);
            else
                connectionString = string.Format("Filename={0};Password='{1}';Connection=shared",
                    dbSettings["dbFilePath"], dbSettings["dbPass"]);
            return connectionString;
        }
    }
}