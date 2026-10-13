# Módulos TechHub

## Entregues

1. Dashboard — métricas locais, nota de saúde e alertas.
2. Impressoras — inventário WMI e instalação por IP.
3. Rede — descoberta consentida, OUI offline e classificação.
4. Drivers — inventário, problemas, assinatura/hash, backup e INF offline.
5. Limpeza — prévia Seguro/Profundo e exclusão catalogada.
6. Diagnóstico — discos, eventos, SFC e DISM.
7. Segurança — Defender, firewall, BitLocker, TPM, Secure Boot e administradores.
8. Atendimento — tickets, comentários e ZIP de diagnóstico.
9. Relatórios — CSV e JSON.
10. Software — inventário do registro e consulta/upgrade por winget.
11. Automação — playbooks locais aprovados.
12. Assistente — regras offline em português.
13. CLI/Extras — comandos de automação, agente REST protegido e calculadora IPv4/CIDR.

## Limites deliberados

- O agente REST não executa scripts ou alterações remotas.
- O assistente não envia dados a LLM e não promete diagnóstico médico ou de segurança além das regras locais.
- Downloads de drivers de terceiros, execução de PowerShell arbitrário, desativação de controles de segurança e exclusões fora do catálogo não são aceitos.
- PDF/XLSX, sincronização REST central, chat remoto, TLS automatizado, auto-update e assinatura de código exigem configuração de produto/infraestrutura que não está presente neste ambiente; CSV/JSON e agente autenticado são as implementações verificáveis entregues.
