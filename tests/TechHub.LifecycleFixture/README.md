# Ciclo de vida WPF real

Executável de teste separado, referência direta a `TechHub.App`. Não pertence à solução nem à publicação. Não substituir o host, DI, janela ou dashboard reais. A fixture cria `portable.flag` antes de instanciar `App`, define `Application.ResourceAssembly`, chama `InitializeComponent()` e `Run()`. Um `DispatcherTimer` aguarda janela carregada, VM sem trabalho ativo e dashboard atualizado; então chama `MainWindow.Close()` ou o método internal `ShutdownForUpdateAsync()`.

## Isolamento em modo portátil

Em produção, `DesktopUpdateInstaller.LastResultMessage` retorna `null` em `StorageMode.Portable`, sem chamar `ReadPreviousResult()`. A fixture cria `portable.flag` antes de iniciar o App, exige o modo portátil e confirma o caminho isolado do banco. O bloqueio antigo baseado em `UpdatePolicy.StagingRoot` foi removido: não é necessário redirecionar resultados para `Data/Updates`, pois essa leitura não ocorre no modo portátil. A fixture não calcula nem consulta caminhos de atualização no LocalAppData e preserva o instalador e o host reais. Está pronta para execução pelo coordenador; a aprovação do ciclo de vida depende dos testes abaixo.

## Execução pelo coordenador (Windows, sessão gráfica)

Diretório do repositório confirmado nesta entrega:
`C:\Users\marco\.codex\.chatgpt-projects\g-p-6a9dc0b186148191be860cf794845aa6\TechHub`

Nenhum build/teste foi executado nesta entrega. O coordenador deve preparar os assets de restore da fixture conforme sua política de rede/cache, e serializar a execução com outros builds/testes. O script usa `build --no-restore`, sem publicar, instalar ou baixar atualizações. Com os assets preparados:

```powershell
& 'C:\Users\marco\.codex\.chatgpt-projects\g-p-6a9dc0b186148191be860cf794845aa6\TechHub\scripts\Test-AppLifecycle.ps1' -ExpectedVersion '1.3.0'
```

Se o SDK estiver em outro local, fornecer `-DotnetPath 'C:\caminho\dotnet.exe'`. A versão esperada é da assembly App e das VMs reais; não é sobrescrita pelo script. Para preparar somente o build após restore (sem iniciar):

```powershell
& 'C:\Users\marco\.codex\dotnet-sdk-8\dotnet.exe' build 'C:\Users\marco\.codex\.chatgpt-projects\g-p-6a9dc0b186148191be860cf794845aa6\TechHub\tests\TechHub.LifecycleFixture\TechHub.LifecycleFixture.csproj' -c Release --no-restore -m:1 -p:UseSharedCompilation=false
```

O script imprime o caminho exato do executável iniciado, em `artifacts/lifecycle/GUID/normal-close/TechHub.LifecycleFixture.exe` e `artifacts/lifecycle/GUID/update-shutdown/TechHub.LifecycleFixture.exe`. As cópias provêm exclusivamente do novo output de build em `GUID/build`. Cada caso tem banco novo `Data/techhub.db`, marcador, stdout/stderr e `lifecycle-evidence.json`; o resumo fica em `GUID/process-results.json`. Não reutilizar os diretórios nem distribuir esses binários.

O limite de 25 segundos cobre todo o processo, incluindo inicialização, consulta real de leitura do dashboard, shutdown e retorno de Main. O script exige saída 0, evidência literal `Run retornou`, versão correspondente e esquema SQLite real `audit_log` no banco isolado. `RunReturned` sozinho não prova saída do processo: o script também aguarda a saída pelo handle retornado ao iniciar. Os processos são sequenciais e iniciados com `WindowStyle Hidden`; a janela WPF real pode aparecer. Não há UIAutomation, controle Win32, ações de manutenção nem execução do instalador/helper.

Timeout é falha: registra PID/caminho, preserva evidências e deixa o processo vivo, sem kill e sem iniciar o próximo caso. Não testar enquanto houver atualização real em andamento: o App respeita o mutex de produção e pode exibir diálogo, que a fixture não controla. Consultas lentas ou diálogos também podem consumir o prazo; isso não deve ser convertido em sucesso nem contornado por flags de produção. O caminho update testa o método de encerramento, não download, handoff ou instalação de uma atualização.
