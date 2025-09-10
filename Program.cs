using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Data.SqlClient;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SimpleTimeService
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices(services =>
                {
                    services.AddHostedService<TimeService>();
                })
                .Build();

            await host.RunAsync();
        }

        public class Config
        {
            public string RunAt { get; set; }
            public string ApiUrl { get; set; }
            public string ConnectionString { get; set; }
            public string Mode { get; set; }
            public string StartDate { get; set; }
            public string EndDate { get; set; }
            public string ResendDocs { get; set; }
            public string ResendFrom { get; set; }
            public string ResendTo { get; set; }
            public AuthorizationConfig Authorization { get; set; }
        }

        public class AuthorizationConfig
        {
            public string username { get; set; }
            public string password { get; set; }
            public string company { get; set; }
            public string instance { get; set; }
            public string grant_type { get; set; }
            public string line { get; set; }
            public string secondToken { get; set; }

            // Adicione isto:
            public string firstToken { get; set; }
        }

        public class TimeService : BackgroundService
        {
            private Timer _timer;
            private Config _config;
            private bool _hasExecuted = false;

            public override async Task StartAsync(CancellationToken cancellationToken)
            {
                Notas.Log("Serviço iniciando...");
                await LoadConfigAsync();
                OnStart();
                await base.StartAsync(cancellationToken);
            }

            protected override Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
                return Task.CompletedTask;
            }

            public override Task StopAsync(CancellationToken cancellationToken)
            {
                OnStop();
                _timer?.Change(Timeout.Infinite, 0);
                return base.StopAsync(cancellationToken);
            }

            public override void Dispose()
            {
                _timer?.Dispose();
                base.Dispose();
            }

            private void OnStart()
            {
                Console.WriteLine("Service started.");
                Notas.Log("Serviço iniciado.");
            }

            private void OnStop()
            {
                Console.WriteLine("Service stopped.");
                Notas.Log("Serviço parado.");
            }

            private async Task LoadConfigAsync()
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                    string json = await File.ReadAllTextAsync(path);

                    _config = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (_config == null || string.IsNullOrWhiteSpace(_config.RunAt) || string.IsNullOrWhiteSpace(_config.ApiUrl))
                        throw new Exception("Valores inválidos ou ausentes no config.");

                    Notas.Log("Configuração carregada com sucesso.");
                }
                catch (Exception ex)
                {
                    string msg = $"Erro ao carregar configuração: {ex.Message}";
                    Console.WriteLine(msg);
                    Notas.Log(msg);
                    Environment.Exit(1);
                }
            }

            private async void DoWork(object state)
            {
                string currentTime = DateTime.Now.ToString("HH:mm");

                if (currentTime == _config.RunAt)
                {
                    if (!_hasExecuted)
                    {
                        Notas.Log($"Executando tarefa agendada para {currentTime}.");
                        await CallApiAsync();
                        _hasExecuted = true;
                    }
                }
                else
                {
                    _hasExecuted = false;
                }
            }

            private async Task CallApiAsync(List<(string Nome, string Ref, string Documento, string SAFT, string SerieFiscal, string Entidade, DateTime DataVenc, string CondPag, string DadosAssinatura, string ModPag, double TotalMerc, double TotalIva, string NumContribuinte, string Morada, string CodigoPostal, string Localidade, string Distrito, string Pais, int Numero, string NomeCaixa)> documentosPendentes = null)
            {
                try
                {
                    using var client = new HttpClient();
                    var url = $"{_config.ApiUrl}/token";

                    var content = new FormUrlEncodedContent(new[]
                    {
            new KeyValuePair<string, string>("username", _config.Authorization.username),
            new KeyValuePair<string, string>("password", _config.Authorization.password),
            new KeyValuePair<string, string>("company", _config.Authorization.company),
            new KeyValuePair<string, string>("instance", _config.Authorization.instance),
            new KeyValuePair<string, string>("grant_type", _config.Authorization.grant_type),
            new KeyValuePair<string, string>("line", _config.Authorization.line)
        });

                    Notas.Log("Enviando requisição para API...");

                    var response = await client.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(responseBody);
                        string accessToken = doc.RootElement.GetProperty("access_token").GetString();

                        Notas.Log($"API respondeu com sucesso. Novo token: {accessToken}");

                        _config.Authorization.secondToken = accessToken;

                        string updatedJson = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync("config.json", updatedJson);

                        Notas.Log("secondToken salvo em config.json com sucesso.");

                        // Retentar envio após renovar token
                        await CreateDocumentAsync();
                    }
                    else
                    {
                        string failMsg = $"Erro na resposta da API: {response.StatusCode}";
                        Console.WriteLine(failMsg);
                        Notas.Log(failMsg);
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Exceção durante chamada de API: {ex.Message}";
                    Console.WriteLine(errorMsg);
                    Notas.Log(errorMsg);
                }
            }


            private async Task CreateDocumentAsync()
            {
                try
                {
                    string connectionString = _config.ConnectionString;

                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

                    DateTime startDate, endDate;

                    if (_config.Mode.Equals("Diario", StringComparison.OrdinalIgnoreCase))
                    {
                        var diaAnterior = DateTime.Today.AddDays(-1);
                        startDate = diaAnterior.Date;
                        endDate = diaAnterior.Date;
                    }
                    else if (_config.Mode.Equals("Personalizado", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!DateTime.TryParse(_config.StartDate, out startDate) ||
                            !DateTime.TryParse(_config.EndDate, out endDate))
                        {
                            Notas.Log("Datas inválidas no modo Personalizado.");
                            return;
                        }
                    }
                    else
                    {
                        Notas.Log($"Modo '{_config.Mode}' não reconhecido. Cancelando operação.");
                        return;
                    }

                    var selectQuery = @"
            SELECT docType, workdate, designation, quantity
            FROM syssevero.dbo.V_SysSalesByProduct
            WHERE CAST([workdate] AS DATE) BETWEEN @startDate AND @endDate";

                    using var cmd = new SqlCommand(selectQuery, connection);
                    cmd.Parameters.AddWithValue("@startDate", startDate);
                    cmd.Parameters.AddWithValue("@endDate", endDate);

                    using var reader = await cmd.ExecuteReaderAsync();

                    var documentos = new Dictionary<string, dynamic>();

                    while (await reader.ReadAsync())
                    {
                        string docType = reader["docType"].ToString();
                        DateTime workdate = Convert.ToDateTime(reader["workdate"]);
                        string designation = reader["designation"].ToString();
                        int quantity = Convert.ToInt32(reader["quantity"]);

                        string key = $"{docType}_{workdate:yyyyMMdd}";

                        if (!documentos.ContainsKey(key))
                        {
                            documentos[key] = new
                            {
                                TipoDoc = docType,
                                Serie = workdate.ToString("yyyy"),      // ✅ apenas o ano
                                Data = workdate.ToString("yyyy-MM-dd"), // ✅ só a data
                                Linhas = new List<object>()
                            };
                        }

                        ((List<object>)documentos[key].Linhas).Add(new
                        {
                            Artigo = designation,
                            Quantidade = quantity
                        });
                    }
                    reader.Close();

                    if (!documentos.Any())
                    {
                        Notas.Log($"Nenhum documento encontrado entre {startDate:yyyy-MM-dd} e {endDate:yyyy-MM-dd}.");
                        return;
                    }

                    // ✅ Agora envia para a API
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.Authorization.secondToken);

                    var url = $"{_config.ApiUrl}/palacete/Internos/";

                    foreach (var doc in documentos.Values)
                    {
                        string jsonBody = JsonSerializer.Serialize(doc, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            WriteIndented = true
                        });

                        Notas.Log($"JSON enviado:\n{jsonBody}");

                        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                        var response = await client.PostAsync(url, content);
                        string apiResponse = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            Notas.Log("✅ Documento criado com sucesso.");
                        }
                        else
                        {
                            Notas.Log($"❌ Erro ao criar documento: {response.StatusCode}");
                        }

                        Notas.Log($"Resposta da API: {apiResponse}");
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Exceção ao criar documentos: {ex.Message}";
                    Console.WriteLine(errorMsg);
                    Notas.Log(errorMsg);
                }
            }

        }
    }
}
