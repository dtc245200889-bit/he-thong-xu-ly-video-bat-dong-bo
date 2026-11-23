using System.Net.Http;
using System.Net.Http.Json;

using var client = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5000")
};

try
{
    Console.WriteLine("Dang goi API lay danh sach video...");

    var response = await client.GetAsync("/videos");
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();

    Console.WriteLine("Ket qua tu API:");
    Console.WriteLine(content);
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Loi khi goi API: {ex.Message}");
}
