using System;
using System.IO;
using System.Text.Json;
using TreePad.Models;

namespace TreePad.Services
{
    public class SessionService
    {
        private readonly string _sessionFilePath;

        public SessionService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "TreePad");
            Directory.CreateDirectory(appFolder);
            _sessionFilePath = Path.Combine(appFolder, "session.json");
        }

        public SessionConfig LoadSession()
        {
            try
            {
                if (File.Exists(_sessionFilePath))
                {
                    string json = File.ReadAllText(_sessionFilePath);
                    var config = JsonSerializer.Deserialize<SessionConfig>(json);
                    if (config != null)
                    {
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load session: {ex.Message}");
            }

            return new SessionConfig();
        }

        public void SaveSession(SessionConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                string tempPath = _sessionFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _sessionFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
            }
        }
    }
}
