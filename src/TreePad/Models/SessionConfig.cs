using System;
using System.Collections.Generic;

namespace TreePad.Models
{
    public class SessionConfig
    {
        public string? StorageDirectory { get; set; }
        public string? LastSelectedFilePath { get; set; }
        public List<string> ExpandedFolders { get; set; } = new();
        public List<string> OpenTabs { get; set; } = new();
        public double WindowLeft { get; set; } = 150;
        public double WindowTop { get; set; } = 100;
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 720;
        public bool IsMaximized { get; set; } = false;
        public double SidebarWidth { get; set; } = 280;
        public bool IsSidebarVisible { get; set; } = true;
        public int CaretIndex { get; set; } = 0;
        public string Theme { get; set; } = "System";
        public double FontSize { get; set; } = 15.0;
        public bool WordWrap { get; set; } = true;
        public string Language { get; set; } = "Auto";
    }
}
