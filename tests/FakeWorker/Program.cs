using System.Text.Json;
string? line;
while ((line = Console.ReadLine()) != null)
{
    using var doc = JsonDocument.Parse(line);
    var request = doc.RootElement;
    var method = request.GetProperty("method").GetString();
    var id = request.GetProperty("requestId").GetString();
    if (method != "hello")
    {
        File.AppendAllText(Environment.GetEnvironmentVariable("TIA_TEST_DISPATCH_LOG")!, method + "\n");
        Thread.Sleep(1800);
        if (Environment.GetEnvironmentVariable("TIA_TEST_BAD_ID") == "1") id = "wrong-id";
    }
    Console.WriteLine(JsonSerializer.Serialize(new { requestId = id, protocolVersion = "0.2", success = true, payloadJson = "{\"connected\":true}" }));
    Console.Out.Flush();
}
