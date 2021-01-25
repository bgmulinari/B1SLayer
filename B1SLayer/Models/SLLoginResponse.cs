using System;

namespace B1SLayer
{
    public class SLLoginResponse
    {
        public string SessionId { get; set; }
        public string Version { get; set; }
        public int SessionTimeout { get; set; }
        public DateTime LastLogin { get; set; }
    }
}