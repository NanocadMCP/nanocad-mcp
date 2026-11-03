using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

internal static class DiscoveryRetentionTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("discovery finds a live bridge beyond 64 stale lifetime descriptors",()=>
        {
            string prior=Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!;
            try
            {
                Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME",Path.Combine(prior,"stale-discovery"));
                Directory.CreateDirectory(Discovery.Instances);
                for(int i=0;i<100;i++)
                {
                    string id=Guid.NewGuid().ToString("D");
                    var stale=Discovery.Describe(id,"test") with{process_start_utc_ticks=1};
                    File.WriteAllText(Path.Combine(Discovery.Instances,id+".json"),JsonSerializer.Serialize(stale,Wire.Json));
                }
                // Choose the actual 81st entry; no filesystem sorting assumption.
                string path=Directory.EnumerateFiles(Discovery.Instances,"*.json").ElementAt(80);
                var live=Discovery.Describe(Path.GetFileNameWithoutExtension(path),"test");
                File.WriteAllText(path,JsonSerializer.Serialize(live,Wire.Json));
                var found=Discovery.List();
                if(found.Length!=1 || found[0]!=live)throw new Exception("Live bridge hidden by stale discovery entries.");
            }
            finally{Environment.SetEnvironmentVariable("NANOCAD_MCP_HOME",prior);}
            return Task.CompletedTask;
        });
    }
}
