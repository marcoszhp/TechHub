# TechHub — cache do projeto

Atualizado em: 2026-10-14

## Estado atual — atualização integrada (2026-10-14)

- Implementado o fluxo de atualização no Dashboard: consulta a release mais recente do GitHub, compara a versão instalada, baixa o instalador sem administrador pelo R2 e abre o instalador sob comando do usuário.
- Versão do aplicativo e dos instaladores atualizada para `1.2.0`; os artefatos foram compilados e os checksums gerados.
- Publicação em R2 preparada em `releases/1.2.0/`; a página `downloads/index.html` aponta para os novos arquivos.
- Release GitHub `v1.2.0` publicada com os quatro artefatos; commit `b361262` e tag `v1.2.0` enviados.
- Cloudflare Pages atualizado em `https://21ab5b22.techhub-downloads.pages.dev`; domínio estável permanece `https://c6f30a0b.techhub-downloads.pages.dev/`.

## Workspace — entrega focada (2026-10-14)

- Criados `src/TechHub.App/Views/WorkspaceView.xaml` e `.xaml.cs`: seis seções por DataTriggers sobre o singleton existente, sem alterar Section no Loaded; formulários rotulados, seleção/busca/gravação/novo para clientes e equipamentos, PC e histórico readonly, ferramentas allowlist, console fixo com scroll/cancelamento, avisos de restauração separada e abertura de caminhos.
- Criado `tests/TechHub.Tests/WorkspaceTests.cs`: 44 testes passaram em execução elevada e filtrada (armazenamento, ownership, snapshot WAL/JSON, proteção de dados ativos, ZIP inseguro/duplicado/symlink, manifesto/banco inválidos, validação de host e argumentos allowlist sem iniciar processos).
- Build do App passou: 0 avisos e 0 erros. Nenhuma alteração em shell/App/DI/Styles/rede nesta entrega. DI existente: `WorkspaceRegistration.AddTechHubWorkspace()` já chamado pela infraestrutura; `WorkspaceViewModel` já singleton no App.
- Inspeção visual interativa ainda não realizada nesta entrega. Encerrado após verificação focada conforme pedido; não publicar nem ampliar escopo.

## Em andamento — versão 1.1 (não publicada)

- Pedido atual: interface inspirada na foto, mapa de rede funcional, central técnica e download no Cloudflare. Contrato em `docs/TECHHUB-1.1-IMPLEMENTATION-PROMPT.md`.
- Alterações locais ainda sem build/testes: shell azul-escuro e tema explícito WPF; título arrastável preservado; módulos sob demanda; correções de campos/layout; métricas e controles de segurança desconhecidos não devem aparecer como zero/desativado.
- Serviços e VMs de rede/Workspace criados parcialmente. Faltavam as duas Views e testes ao retomar. Integrar DI, compilar e inspecionar interface real antes de publicar.
- Instalador ajustado para versão1.1 e sem portable.flag/Data; publish usa destino novo release-1.1.0. Pacotes antigos NÃO foram substituídos nesta etapa.
- Cloudflare autenticado via Wrangler4.150.0; Pages disponível, R2 não ativado (API10042). Plano: página no Cloudflare + binários no GitHub Releases, sem ativar cobrança. Nenhuma implantação realizada ainda.
- Repositório público autorizado: https://github.com/marcoszhp/TechHub. HEAD anterior a38c481. Alterações de título anteriores preservadas.
- A rodada anterior foi interrompida ao atingir5% restante. Ao retomar, uso13% da janela5h e51% semanal; não resgatados resets/créditos.
- O histórico de builds abaixo refere-se à versão anterior, NÃO valida o trabalho1.1.

## Estado verificado em 2026-10-14

- Build App Release passou com 0 avisos/0 erros; suíte completa passou 108/108.
- Correção visual do mapa: bindings OneWay nos textos somente leitura evitaram encerramento ao abrir a tela; App compilado depois da correção.
- Processo da interface permaneceu respondendo aberto no Windows durante a inspeção. Clique automatizado no mapa foi interrompido por interação do usuário; não foi executada varredura de rede.
- Publicação GitHub: `https://github.com/marcoszhp/TechHub/releases/tag/v1.1.0`, assets Setup, Portable ZIP e SHA256SUMS enviados e verificados por tamanho; release pública.
- Página Cloudflare Pages publicada em `https://c6f30a0b.techhub-downloads.pages.dev`; verificação via Wrangler confirmou deployment. Request PowerShell local teve handshake TLS e a ferramenta web não abriu a URL, portanto manter essa limitação registrada.
- R2 não ativado. Binaries ficam no GitHub Releases porque Pages limita asset individual a 25 MiB.
- Qwen: memória de `local-qwen-executor` reutilizada; Ollama 0.40.2 foi iniciado e modelo configurado confirmado. Nesta rodada o Qwen não recebeu alteração do TechHub; a revisão e integração permaneceram com o coordenador.
- Próximo passo: se necessário, revisar a página Cloudflare por navegador normal, abrir a release no computador e testar uma varredura somente após consentimento explícito na tela.

## Objetivo

Implementar e validar as fases do aplicativo desktop TechHub em C#/.NET 8, WPF + WPF-UI, MVVM, DI/Hosting, SQLite, Serilog e PowerShell 7 embarcado.

## Estado atual

- A Fase 1 foi implementada em `src/TechHub.Core`, `src/TechHub.Infrastructure`, `src/TechHub.PowerShell` e `src/TechHub.App`; os testes estão em `tests/TechHub.Tests`.
- O SDK .NET 8 foi instalado em `C:\Users\marco\.codex\dotnet-sdk-8`; o projeto usa `.dotnet-home` e `.nuget` locais para evitar dependência de perfil externo.
- A solução compila em Debug e Release com 0 warnings/0 errors.
- O smoke test manual validou bloqueio sem consentimento, execução/auditoria com consentimento e resolução OUI.
- A Fase 2 foi integrada: Drivers, Limpeza, Diagnóstico e Segurança, com telas WPF, contratos no Core, serviços Windows e ActionRunner.
- Drivers validam inventário WMI, assinatura/hash de INF local, backup e instalação offline; Limpeza usa prévia imutável e revalidação por handle.
- Diagnóstico consulta WMI/Event Viewer e oferece SFC/DISM; Segurança consulta Defender, firewall, BitLocker, TPM, Secure Boot e administradores locais.
- A Fase 3 foi integrada: tickets SQLite, comentários, coleta de diagnóstico em ZIP, exportação CSV/JSON e inventário/upgrade de software via winget.
- A Fase 4 foi integrada: playbooks JSON locais, assistente offline, CLI `techhub`, agente REST com token e calculadora IPv4/CIDR.

## Decisões registradas

- Arquitetura em App, Core, Infrastructure e PowerShell; o Core não referencia WPF.
- Ações de sistema passam pelo ActionRunner. Prévia antes da execução, consentimento explícito para mudanças, auditoria sem segredos desnecessários e undo específico apenas quando seguro.
- `portable.flag` ao lado da aplicação habilita dados sob `Data` ao lado dela; sem o marcador, usar `%LocalAppData%\TechHub`. Falhas de escrita devem ser explícitas, sem fallback silencioso.
- O PowerShell foi isolado no projeto `TechHub.PowerShell`; a infraestrutura depende apenas da interface `IPowerShellExecutor`.
- A publicação portátil usa `publish.ps1`, `win-x64`, self-contained, single-file e `portable.flag`.

## Validação e limitações

- Restore e build da solução foram executados com sucesso, sem warnings ou erros.
- A publicação autocontida `win-x64` com single-file, ReadyToRun e `portable.flag` foi gerada em `artifacts/portable`, com CLI em `artifacts/cli` e agente em `artifacts/agent`.
- O instalador Inno Setup foi compilado em `artifacts/installer/TechHub-Setup-1.0.0.exe`; o script `publish.ps1` fixa MSBuild em processo único para evitar a falha silenciosa do ambiente.
- O banco SQLite de demonstração está em `samples/TechHub-sample.db`, acompanhado de `samples/sample-data.json`.
- O pacote-fonte sem caches está em `artifacts/TechHub-all-phases-source.zip`.
- O smoke test manual executável passou.
- O VSTest não completou neste ambiente: o testhost falhou ao abrir socket local com `SocketException 10013`; isso é limitação do ambiente, não falha de compilação dos testes.
- Os testes de parsing dos serviços da Fase 2 foram adicionados e compilam; a execução permanece sujeita à mesma limitação do VSTest.
- Validar manualmente em Windows a janela WPF, a instalação de uma impressora de teste, a descoberta na rede autorizada e o agente com certificado TLS real.

## Próximo passo

Validar em uma sessão Windows sem a política de bloqueio de sockets: executar VSTest, abrir a interface WPF, verificar a instalação de uma impressora de teste e configurar o agente com certificado TLS real. A implementação entregue mantém o agente mutável bloqueado até essa infraestrutura ser definida.
