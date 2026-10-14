# TechHub

TechHub é um aplicativo desktop para diagnóstico e manutenção de computadores Windows. As Fases 1 e 2 estão implementadas em C#/.NET 8 com WPF, WPF-UI, MVVM, Generic Host/DI, SQLite, Serilog e PowerShell 7 embarcado.

## Stack

- C# e .NET 8 com aplicação WPF.
- WPF-UI para componentes da interface.
- CommunityToolkit.Mvvm para MVVM.
- Generic Host e injeção de dependências.
- SQLite para auditoria local.
- Serilog para logs estruturados em arquivo.
- System.Management.Automation para PowerShell 7 embarcado.

## Arquitetura

- **TechHub.App** (`src/TechHub.App`): aplicação WPF, composição do Host/DI, logging, janela e navegação.
- **TechHub.Core** (`src/TechHub.Core`): regras e contratos independentes de WPF.
- **TechHub.Infrastructure** (`src/TechHub.Infrastructure`): persistência SQLite, auditoria, métricas, rede, impressoras, drivers, limpeza, diagnóstico e segurança do Windows.
- **TechHub.PowerShell** (`src/TechHub.PowerShell`): executor PowerShell 7 embarcado.
- **TechHub.Cli** (`src/TechHub.Cli`): modo console `techhub --cli` para automação local e scripts.
- **TechHub.Agent** (`src/TechHub.Agent`): agente REST Windows com token obrigatório e inventário somente leitura.
- **TechHub.Tests** (`tests/TechHub.Tests`): testes unitários das regras do ActionRunner e da rede.

O Core não referencia WPF. A interface depende de serviços e contratos, e ações de alteração passam pelo ActionRunner.

## ActionRunner e segurança operacional

O ActionRunner é o ponto de entrada para ações que consultem ou alterem o computador. O fluxo implementado valida a ação, gera uma prévia compreensível, exige consentimento explícito para alterações, verifica elevação quando necessária, cria ponto de restauração quando solicitado, executa com cancelamento e registra resultado e erros em SQLite. Undo só é exposto quando a ação fornece uma reversão segura.

## Fase 1 — implementada

- Estrutura da solução, Host/DI, logging e inicialização da janela.
- Dashboard com CPU, memória, disco, sistema operacional, privilégios e alertas.
- Impressoras: inventário WMI e instalação por endereço IP usando o driver Microsoft IPP Class Driver.
- Rede: consentimento explícito, descoberta de dispositivos na sub-rede local, fabricante por OUI offline e classificação heurística.
- Modo portátil por `portable.flag`, com `Data` e `Logs` junto ao executável.

Os critérios verificáveis estão em [Fase 1](docs/PHASE-1-ACCEPTANCE.md).

## Fase 2 — implementada

- Drivers: inventário WMI, identificação de problemas, verificação de assinatura/hash de pacotes INF locais, backup com PnPUtil e instalação offline confirmada.
- Limpeza: análise Seguro/Profundo com catálogo restrito, prévia de arquivos/bytes/motivos, proteção contra reparse points e revalidação antes da exclusão.
- Diagnóstico: discos, eventos críticos recentes, uptime e reparos SFC/DISM protegidos por confirmação, privilégio e ponto de restauração.
- Segurança: Defender, proteção em tempo real, firewall, BitLocker, TPM, Secure Boot e administradores locais; varredura rápida confirmada.
- Distribuição: além do instalador administrativo, há um instalador **TechHub User** que instala em `%LocalAppData%` sem UAC. Ele permite as funções de leitura, diagnóstico, rede, chamados, relatórios e limpeza do perfil do usuário; operações protegidas pelo Windows continuam identificadas e bloqueadas com segurança.

Os critérios verificáveis estão em [Fase 2](docs/PHASE-2-ACCEPTANCE.md).

## Fase 3 — implementada

- Central de Atendimento: chamados locais em SQLite, categorias, prioridades, status, comentários e coleta de diagnóstico em ZIP.
- Relatórios: exportação de chamados para CSV e JSON em caminho escolhido pelo técnico.
- Atualizações/software: inventário do registro do Windows e consulta/upgrade via winget com confirmação pelo ActionRunner.

Os critérios verificáveis estão em [Fase 3](docs/PHASE-3-ACCEPTANCE.md).

## Fase 4 — implementada

- Automação: playbooks JSON locais com ações aprovadas, confirmação e auditoria.
- Multi-máquinas: `TechHub.Agent` expõe health check e inventário protegido por `TECHHUB_AGENT_TOKEN`; ações remotas mutáveis permanecem bloqueadas.
- Assistente: diagnóstico offline em português por regras, sem envio de dados a LLM.
- CLI: inventário, limpeza com prévia/confirmatório, diagnóstico, tickets, automação, software e calculadora de sub-rede.
- Extras: calculadora IPv4/CIDR e execução de playbooks/relatórios locais.

Os critérios verificáveis estão em [Fase 4](docs/PHASE-4-ACCEPTANCE.md).

## Execução e validação

Pré-requisitos: Windows e SDK do .NET 8. Na raiz do repositório:

```powershell
dotnet restore .\TechHub.sln --configfile .\NuGet.Config
dotnet build .\TechHub.sln --configuration Release
dotnet test .\tests\TechHub.Tests\TechHub.Tests.csproj --configuration Debug
dotnet run --project .\src\TechHub.App\TechHub.App.csproj
```

Para gerar uma pasta portátil autocontida, use `publish.ps1`. O script publica `win-x64`, copia `portable.flag` e mantém o estado em `Data`:

```powershell
.\publish.ps1 -Configuration Release
# Opcional, se o Inno Setup estiver instalado:
.\publish.ps1 -Configuration Release -Installer
```

Os artefatos ficam em `artifacts\portable`, `artifacts\cli`, `artifacts\agent` e `artifacts\installer`. O projeto também inclui `samples\TechHub-sample.db` e `samples\sample-data.json` como banco e dados iniciais de demonstração.

O pacote-fonte distribuível é gerado como `artifacts\TechHub-all-phases-source.zip`, sem caches de build e sem os binários autocontidos.

Exemplos da CLI:

```powershell
techhub.exe --cli inventory
techhub.exe --cli clean --safe
techhub.exe --cli clean --safe --confirm
techhub.exe --cli diagnose "sem internet"
techhub.exe --cli subnet 192.168.1.0/24
techhub.exe --cli automation list
```

Para iniciar o agente, defina um token fora do código e use HTTPS/rede protegida na implantação:

```powershell
$env:TECHHUB_AGENT_TOKEN = "um-token-longo-e-aleatorio"
dotnet run --project .\src\TechHub.Agent\TechHub.Agent.csproj --urls https://127.0.0.1:7443
```

O agente só expõe inventário protegido nesta fase; não executa scripts ou ações remotas mutáveis.

O aplicativo exige Windows compatível com WPF. Em ambientes restritos que bloqueiam sockets locais, o VSTest pode falhar antes de carregar os testes com `SocketException 10013`; nesse caso, use uma sessão Windows sem essa política para a validação automatizada.

## Modo portátil e dados

O modo portátil é ativado pela presença do arquivo `portable.flag` no diretório da aplicação. A aplicação detecta o marcador no início e escolhe `Data` ao lado dele para banco SQLite, logs e demais dados locais. Sem o marcador, usa `%LocalAppData%\TechHub`.

O diretório portátil precisa ser gravável. Falhas de permissão/escrita são registradas e não provocam fallback silencioso. Consulte [Módulos](docs/MODULES.md) para os limites deliberados e integrações que dependem de infraestrutura externa.
