# Fase 4 — critérios de aceite

## Automação

- [x] Playbooks são persistidos em `Data/automations.json`.
- [x] Só existem ações explicitamente modeladas; não há execução arbitrária de PowerShell.
- [x] Cada etapa passa pelo ActionRunner e etapas mutáveis exigem confirmação.
- [x] A execução para na primeira falha e retorna as saídas já produzidas.

## Multi-máquinas

- [x] `TechHub.Agent` é um processo ASP.NET Core separado.
- [x] O endpoint de health é simples e o inventário exige `X-TechHub-Token` comparado em tempo constante.
- [x] O agente não oferece execução remota mutável nesta fase.
- [ ] TLS com certificado gerenciado, cadastro de máquinas, grupos, alertas e sincronização central ainda exigem implantação de infraestrutura e ficam bloqueados por decisão de segurança/arquitetura.

## Assistente e CLI

- [x] O assistente offline responde a sintomas comuns em português usando regras determinísticas.
- [x] A CLI suporta inventário, prévia/execução de limpeza, diagnóstico, tickets, playbooks, software e CIDR.
- [x] `--confirm` é necessário para a limpeza destrutiva na CLI.
- [x] A saída dos comandos é JSON para integração com scripts.

## Extras

- [x] Calculadora IPv4/CIDR sem dependência de rede externa.
- [x] Busca e ações seguem as mesmas interfaces do aplicativo, sem duplicar regra de segurança.

## Validação

```powershell
dotnet build .\TechHub.sln --configuration Release
dotnet run --project .\src\TechHub.Cli\TechHub.Cli.csproj -- --cli subnet 192.168.1.0/24
dotnet run --project .\src\TechHub.Cli\TechHub.Cli.csproj -- --cli diagnose "sem internet"
```

Build Release e os comandos CLI acima foram validados. O VSTest continua dependendo de uma sessão que permita sockets locais (`SocketException 10013` no ambiente atual).
