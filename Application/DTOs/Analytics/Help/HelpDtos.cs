namespace Application.DTOs.Analytics.Help
{
    /// <summary><c>computeFor(T)</c> of renderEngineBHelp: the worked Max Permissible example for one tool.</summary>
    public sealed class HelpMaxPermExampleDto
    {
        /// <summary>T as JS prints it (String(T)).</summary>
        public string T { get; set; } = "";
        /// <summary>Effective T = T × (1 + buffer/100), rounded to 2 decimals, as a string ("78.75").</summary>
        public string EffT { get; set; } = "";
        /// <summary>"78.75×379−87,265".</summary>
        public string Calc { get; set; } = "";
        /// <summary>"MAX(0, −57,419) = 0" / "MAX(0, 40,648) = 40,648".</summary>
        public string Result { get; set; } = "";
        public double MaxPermissible { get; set; }
    }

    public sealed class HelpTabDto
    {
        public HelpTabDto() { }
        public HelpTabDto(string id, string title, string fragment) { Id = id; Title = title; Fragment = fragment; }
        /// <summary>"vision", "t1-engA", … (hwTab id).</summary>
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>Static HTML fragment served by the portal: help/hw-{id}.html.</summary>
        public string Fragment { get; set; } = "";
    }

    /// <summary>The live values the "How it works" tabs show (renderEngineBHelp) + the tab list.</summary>
    public sealed class HelpParamsDto
    {
        public string D { get; set; } = "";
        /// <summary>strBuffer||0.</summary>
        public string Buffer { get; set; } = "";
        public string Mix { get; set; } = "";
        public string N { get; set; } = "";
        /// <summary>mixWindowDays||45.</summary>
        public string MixWindowDays { get; set; } = "";
        public double SampleStock { get; set; } = 87265;
        public double SampleRate { get; set; } = 379;
        /// <summary>Tool 1 (T1) example.</summary>
        public HelpMaxPermExampleDto Tool1 { get; set; } = new();
        /// <summary>Tool 2 (T2) example.</summary>
        public HelpMaxPermExampleDto Tool2 { get; set; } = new();
        /// <summary>{{NAME}} placeholder → value, for the help/hw-*.html fragments.</summary>
        public Dictionary<string, string> Placeholders { get; set; } = new();
        public List<HelpTabDto> Tabs { get; set; } = new();
    }
}
