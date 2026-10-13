# Fase 3 — critérios de aceite

## Central de Atendimento

- [x] Chamados são persistidos em SQLite com título, descrição, categoria, prioridade, status, solicitante e timestamps.
- [x] Comentários e mudança para resolvido têm histórico persistente.
- [x] Coleta de diagnóstico cria ZIP com ticket e snapshot do Dashboard em `Data/Attachments` e registra o caminho no chamado.
- [x] A tela lista, cria e atualiza chamados sem depender de serviço externo.

## Relatórios

- [x] Chamados podem ser exportados para CSV UTF-8 com cabeçalho e escaping de campos.
- [x] Chamados podem ser exportados para JSON indentado.
- [x] O destino deve ser absoluto e ter a extensão esperada.

## Atualizações e software

- [x] Inventário de software consulta chaves de desinstalação do usuário e da máquina em visões 32/64-bit.
- [x] Consulta de atualizações usa winget quando disponível e informa indisponibilidade sem inventar resultado.
- [x] `winget upgrade --all` é uma ação elevada de alteração, com prévia, confirmação e auditoria pelo ActionRunner.

## Validação

```powershell
dotnet build .\TechHub.sln --configuration Release
dotnet build .\tests\TechHub.Tests\TechHub.Tests.csproj --configuration Debug
dotnet test .\tests\TechHub.Tests\TechHub.Tests.csproj --configuration Debug
```

Build Release/Debug concluído sem avisos ou erros. A execução do VSTest permanece condicionada à permissão de sockets locais do ambiente.
