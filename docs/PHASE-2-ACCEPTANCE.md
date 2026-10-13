# Fase 2 — critérios de aceite

## Drivers

- [x] Inventário local por WMI com versão, fabricante, INF, assinatura, status e código de problema.
- [x] Dispositivos com problema são identificados sem tratar lista vazia como falha.
- [x] Pacotes INF offline são verificados localmente por catálogo assinado e hash SHA-256.
- [x] Instalação offline e backup são `ActionRequest` elevados, com recursos afetados explícitos e execução pelo ActionRunner.
- [x] O código recusa URL, caminho de rede, links/reparse points e instalação sem assinatura confiável.

## Limpeza

- [x] Existem modos Seguro e Profundo.
- [x] A análise produz prévia com arquivo, bytes, motivo e escopo sem apagar nada.
- [x] Somente raízes de catálogo são analisadas: temporários, caches conhecidos, logs/dumps, Prefetch, downloads do Windows Update e lixeira do usuário.
- [x] Reparse points, arquivos protegidos, hard links e arquivos alterados após a prévia são recusados.
- [x] A exclusão é destrutiva, exige confirmação, auditoria e ponto de restauração quando possível.

## Diagnóstico e reparo

- [x] A coleta consulta o sistema operacional, discos e eventos críticos recentes.
- [x] Falhas de coleta são separadas de listas vazias e geram recomendação acionável.
- [x] SFC e DISM são requests elevados com confirmação e ponto de restauração.

## Segurança

- [x] A tela consulta Defender, proteção em tempo real, firewall, BitLocker, TPM, Secure Boot e administradores locais.
- [x] Alertas são apresentados quando controles estão desativados ou indisponíveis.
- [x] A varredura rápida do Defender exige prévia, confirmação e privilégio.

## Validação

```powershell
dotnet build .\TechHub.sln --configuration Release
dotnet build .\tests\TechHub.Tests\TechHub.Tests.csproj --configuration Debug
dotnet test .\tests\TechHub.Tests\TechHub.Tests.csproj --configuration Debug
```

Os projetos e testes compilam. A execução do VSTest deve ocorrer em uma sessão que permita sockets locais; no ambiente de desenvolvimento atual o testhost foi bloqueado por `SocketException 10013` antes de carregar os testes.
