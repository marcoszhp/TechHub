# Fase 1 — critérios de aceite

Este documento especifica resultados verificáveis para a Fase 1. Os itens abaixo foram implementados; os critérios de execução manual continuam indicados como tal.

## Estrutura e inicialização

- [x] Existe uma solução `TechHub.sln` na raiz com App, Core, Infrastructure, PowerShell e Tests.
- [x] A aplicação é WPF em C#/.NET 8 e usa WPF-UI, MVVM e Generic Host/DI.
- [x] A composição de dependências fica na inicialização; Views e ViewModels dependem de contratos/serviços.
- [x] A janela principal oferece Dashboard, Impressoras e Rede.
- [x] SQLite e Serilog são configurados por serviços registrados e separados da View.

## ActionRunner

- [x] Ações de sistema passam por uma abstração central ActionRunner.
- [x] Antes da execução, o usuário recebe uma prévia com descrição, alvo/escopo e efeitos esperados.
- [x] Ações que alteram estado exigem consentimento explícito antes da execução.
- [x] Resultado, horário, tipo de ação e falha/sucesso ficam registrados em SQLite.
- [x] Erros são registrados e retornados como resultado seguro, sem derrubar a aplicação.
- [x] Undo só é oferecido quando a ação fornece uma reversão segura; ações sem undo deixam isso explícito.
- [x] Cancelamento é propagado ao executor da ação; não há repetição silenciosa.

## Dashboard

- [x] O Dashboard é a tela inicial e apresenta métricas reais obtidas por serviço do Windows.
- [x] Os dados exibidos vêm de serviço explícito; falhas viram alertas de diagnóstico.

## Impressoras

- [x] A tela lista impressoras por WMI e informa falhas da consulta.
- [x] Instalação por IP passa pelo fluxo de prévia, consentimento e ActionRunner.
- [x] A interface expõe somente inventário e instalação por IP implementados nesta fase.

## Rede

- [x] A tela apresenta apenas dispositivos obtidos pela descoberta local e informa indisponibilidade.
- [x] A descoberta é diagnóstica, exige consentimento explícito e não altera o sistema.
- [x] A interface não apresenta a descoberta como prova de segurança ou garantia de conectividade.

## Modo portátil e persistência

- [x] `portable.flag` junto à aplicação seleciona `Data` junto à aplicação para SQLite e logs.
- [x] Sem `portable.flag`, a aplicação seleciona `%LocalAppData%\TechHub`.
- [x] O caminho efetivo é determinístico e não há fallback silencioso após falha de escrita.
- [x] O banco SQLite é criado/aberto sem apagar dados existentes e registra auditoria.
- [x] A documentação informa que o modo portátil exige diretório gravável.

## Validação

Executar na raiz do repositório:

```powershell
dotnet --info
dotnet restore .\TechHub.sln --configfile .\NuGet.Config
dotnet build .\TechHub.sln --configuration Release
dotnet test .\tests\TechHub.Tests\TechHub.Tests.csproj --configuration Debug
dotnet run --project .\src\TechHub.App\TechHub.App.csproj
```

Critério de conclusão: restore/build e o smoke test executável terminam com código zero; o teste VSTest deve ser repetido em uma sessão Windows sem bloqueio de sockets locais. A inspeção manual da janela continua sendo a validação final de interação visual.
