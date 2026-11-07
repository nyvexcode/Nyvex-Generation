# NYVEX GENERATION 3.0 — Professional Edition

Ferramenta para gerar, organizar e consultar usernames do Roblox (WPF, .NET 8, MVVM).

## Como compilar e rodar (Windows 10/11 x64)

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project NyvexGeneration -c Release
```

EXE standalone (também é o que o GitHub Actions faz):

```powershell
dotnet publish NyvexGeneration/NyvexGeneration.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish/NyvexGeneration
```

## GitHub Actions

`.github/workflows/build.yml` roda em `windows-latest`: instala o .NET 8, restaura, compila, executa os testes,
publica o EXE (win-x64, single-file, self-contained), gera `NyvexGeneration-win-x64.zip` e o disponibiliza como
artefato da execução (aba **Actions → execução → Artifacts**). Em tags `v*` o ZIP também é anexado ao Release.

## Estrutura

```
NyvexGeneration.sln
NyvexGeneration/                 App WPF
├── Core/            enums de domínio (LookupStatus, ConnectionState, ...)
├── Models/          LookupResult, GenerationOptions, AppSettings, DTOs do Roblox, ...
├── Services/        gerador, validador, RobloxApiService, CheckCoordinator, exportação, logs, estado
├── Data/            SQLite: banco, repositórios, backup
├── Infrastructure/  AppPaths, DI, tema, diálogos
├── ViewModels/      MVVM (CommunityToolkit.Mvvm)
├── Views/           MainWindow, Dashboard, Gerador, Resultados, Logs, Configurações
├── Helpers/         conversores, formatação de status, janela
├── Resources/       Theme.xaml (preto profundo + roxo neon)
└── Assets/
NyvexGeneration.Tests/           xUnit (sem rede)
.github/workflows/build.yml
```

Dados do usuário: `%LocalAppData%\NyvexGeneration` (banco `nyvex.db`, `logs/`, `backups/`, `exports/`).

## Sobre o Roblox Auto Checker (leia)

- Endpoint usado: `POST https://users.roblox.com/v1/usernames/users` (corpo `{"usernames":[...],"excludeBannedUsers":false}`),
  listado na documentação oficial do Roblox como *Get users by usernames*.
- A documentação **não informa** limite de usernames por requisição nem limites de taxa para esse endpoint, e indica
  autenticação por cookie. Este app **não usa login, cookies ou credenciais**. Se o Roblox responder 401/403/400, a
  consulta aparece como **Consulta inconclusiva** (nunca como "livre"). Lote (50) e intervalo (1,5 s) são conservadores
  e configuráveis.
- Estados exibidos: **Conta encontrada**, **Nenhuma correspondência** (com "disponibilidade de registro NÃO confirmada"),
  **Consulta inconclusiva** (HTTP 429, timeout, resposta inválida, erro HTTP) e **Erro de conexão**.
- "Nenhuma correspondência" **não** significa que o username está livre. O app não automatiza criação de contas,
  login, CAPTCHA ou qualquer mecanismo de proteção.
- Usernames de 3 a 5 caracteres podem ser raros/reservados ou inválidos por regras do Roblox que o app não conhece
  por completo; use o resultado apenas como informação.

## Testes

`dotnet test` cobre: gerador, duplicatas, validação, desserialização JSON, erros HTTP, rate limit (429/Retry-After),
timeout, cancelamento, orquestração de lotes, exportação TXT/CSV, logs (inclusive remoção de segredos) e persistência
SQLite (histórico, favoritos, configurações, estatísticas, backup). Nenhum teste acessa o Roblox.
