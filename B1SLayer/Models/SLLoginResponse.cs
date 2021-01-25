using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Newtonsoft.Json")]
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