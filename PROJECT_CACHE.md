# TechHub — cache do projeto

Atualizado em: 2026-10-14

## Retomada D1 — 14/10/2026, 23:32

- Limite renovado observado (0% usado no início desta retomada), sem resets/créditos acionados pelo agente. Última leitura50% principal/23% semanal; consultar antes da próxima fase grande.
- Código candidato1.3.0 pronto para gerar pacotes; ainda NÃO publicado nesta anotação. Estado anterior abaixo é histórico. Novo aceite e limitações em `docs/ATUALIZADOR-INTEGRADO.md`, seção Retomada.
- 243 testes aprovados (180+63), restore/auditoria sem warnings. Inno real isolado1.2.1→1.3.0 passou: `artifacts/update-integration/6c3895fe8851496c89ded58ef268054f/evidence.json`. GitHub oficial1.2.1 e rejeição de digest adulterado: `artifacts/update-integration/trust-check/trust-evidence.json`. App WPF real abriu e fechou em processos próprios com dados isolados: `artifacts/lifecycle/24c1782235a1427488e4b06b5fe01282`, saídas0 em2,56s/2,46s (normal/update), sem kill.
- Corrigidos e revisados independentemente: retry invalidado antes do mutex, marcadores Global para máquina com ACL explícita e startup recheck, metadatahelper limitado1MiB e semredirect. Portátil não lê resultado da instalação do perfil. Acrescentada confirmação final após download para salvar edições recentes.
- Revisão final PASS condicional, sem novosP0/P1/P2; duas sessões/contasWindows e UACoutroSID não exercitados. Sem teste de clique do consentimento até o helper dentro do app instalado; testes compostos não devem ser apresentados como esse E2E. Todos subagentes fechados.
- Site e notas LOCALMENTE preparados1.3.0; publicar somente após gerar e conferir artefatos sem Data/logs/fixtures. Próximo: commit da fonte candidata, publish App+Updater autocontidos, dois instaladores +ZIP +SHA256; GitHub draft e R2 conferidos antes de tornarreleasepública e implantarPages. Instalação por usuário atual ainda1.2.1 e máquinaantiga1.1.0 preservadas. Nenhum processo TechHub.App estavaaberto no preflight.

## Em andamento — D1 atualizador integrado 1.3.0 (18:41, 14/10/2026)

- Entrega local de notas/site: `downloads/index.html` e `docs/RELEASE-1.3.0.md` preparados com links GitHub/R2 para1.3.0 e limites do atualizador. Não publicados. Conferência objetiva confirmou todos os URLs versionados em1.3.0; ocorrência1.2.1 restante no HTML é a orientação para migração inicial. `git diff --check` passou. WPF de produção continua pendente.

- Pedido mais recente: próximas fases, começando por atualizar pelo app sem reinstalação manual recorrente. Contrato e limites em `docs/ATUALIZADOR-INTEGRADO.md`. Base main limpa59007de; alterações locais **não commitadas/publicadas/instaladas**. Versão pública/instalada continua1.2.1; fonte candidataApp/Updater/Inno1.3.0.
- Implementado: contrato Core UpdateInfo/status/mode/progress; GitHubUpdateService valida release estável/tagcanônica vX.Y.Z, assetexato por modalidade, digest/tamanho, origemHTTPS, downloadcancelável .partial, stagingGUID exclusivo e locks de diretórios contra junctionswap. Detecção suporta somente instalações padrão win-x64 User/Machine com exe/unins000; portable/custom/desconhecido manual.
- Dashboard: cartão escuro, versões, notas, progresso, verificar/cancelar/Atualizar e reiniciar; exclusão de concorrência, erros/retry, consentimento antesdownload. IUpdateInstaller ligadoDI. DesktopUpdateInstaller bloqueia ações/instâncias ativas, backupWorkspace SQLiteonline+JSON, prepara helper, ready/apply-authorized, encerramento aguardandohost. App tem marcadorMutex de execução e bloqueia abertura durante instalação.
- Helper novo `src/TechHub.Updater` (na solução, Core-only, fonteStagingDirectoryGuard linkada daInfra): valida plano/caminhos, mantém pacote aberto FileShare.Read atéexecução, espera identidadePID/start/exepath semkill, revalida releaseGitHub/tag/hash/size, argsInno fixos /SILENT /NORESTART /NOCLOSEAPPLICATIONS /NOFORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS, usersemUAC/machinecomUAC, confirmaversão, result.json e reinício semelevação. Falhas não restauram banco automaticamente. Elevatedapp recusaautoupdate para preservarusuáriooriginal. Ainda não E2E realvalidado.
- Inno User/Machine1.3.0: marcador install-mode.txt case-insensitive, AppMutex e semfechar/reiniciaroutrosapps. publish.ps1 defaultrelease1.3.0 inclui auxiliarselfcontained; **publicação/instaladores ainda não gerados**. Não removerinstalaçãomáquina1.1.0 nemdadosprivados.
- Testes: suítecompleta177Core/Infra +53UI =230aprovados apósduascorreçõesrevisor; TRX `artifacts/test-results-updater-final`. Depois guardsApp/helper e fixtureforeground: focados70+20aprovados, helperbuild0warnings0erros; TRX `artifacts/test-results-updater-guards`. SemreparosWindows/instalaçãodrivers; inclui duasconsultasreaissomenteleitura. Não confundirfakesengine comatualizaçãorealentreversões.
- WPFrealXAML renderizado comdadossintéticos em1080/1360, imagens `artifacts/ui-update-preview/update-*.png`; fixture destaca conteúdoDashboard, nãoéprintdoappinstalado. PrimeirofixturesemForegroundherdado geroutextopreto, corrigidoherançaHubInk etestesrepetidos. Inspeçãovisualfinalaindapendente.
- Revisãoindependente Sol/medium nohelper/coordinator identificou deadline15minimpedindorestartapósinstalllonga e mutexmantidoemfalha. Corrigidos: conclusãoCancellationToken.None +testDeadlineDuringInstallation; WindowsUpdateRuntimeIDisposable emusing dentrotry,liberaantescatch eRestart. Revisorconfirmoua lógica **pelo relato, semrelerpatch**; aprovaçãoindependentefinalaindapendente. Ambosagentesfechados, nenhumativo.
- Restore gerou NU1900 (consulta NuGetvulnerabilidades indisponível/cache), inclusive tenta fora sandbox; nãohouvepackageupgrade. Futuroforce-restore/networkcheck doaudit ainda precisa concluir; não alegar auditorialimpa. Umafalhainicialdebuild porusingSystem.IO emtesteUI foi corrigida. BuildApp/testes atuaispassam.
- Limite atual: primário97%usado (3%restante), secundário15%usado. Regra5%do usuário impõe adiarpróximaetapapesada. Nãoresgatadosresets/nemcréditoscomprados. Exceçãoantiga valia somente publicar1.2.1, nãoD1.
- Próximo apósrecuperaçãodelimite: (1) leitura focada destecontrato+diff; (2) revisarrealhandoff/backup/ready/timeout/arquivoenquantoinstall, compatibilidadeFileShare.Read/Inno e mutex; (3) gerarApp+Updaterautocontidos e instaladores, semdados; (4) testar duasversões, instalaçãofail/cancelUAC/instânciaconcorrente, saídanormalsemprocessoresidual, backup/dados e reinício comtokenusuário, emambienteisolado. PreferirfixtureInno comAppIdseparado e semuninstallregistro; nãoinstalarfixtureemproduçãonemcriarflagdevalidaçãobypassnoprod. ReleaseoficialhelperrevalidaAPI, então planoE2E precisa definir testabilidade sempublicarcandidatainsegura. (5) revisãofreshdospatches e aceitetécnico; sóentão GitHub/R2/Pages. Não alterarsite/sha/release1.2.1 agorapara1.3.0inexistente. (6) instalarbootstrap1.3.0 umavezcomappsfechados/trabalhosalvo; próximasviaapp. D2portátil/custom/periódico/assinaturacódigo/rollbackcompleto pendentes.

## Histórico da publicação anterior

## Estado atual — lote de estabilidade 1.2.1 (14/10/2026)

- Pedido vigente: executar `docs/PROMPT-MESTRE-EVOLUCAO-TECHHUB.md` por etapas, iniciando crashes de Diagnósticos/Drivers e tema exclusivamente escuro.
- Fonte, instaladores, release pública e site estão na versão1.2.1. Publicação confirmada após autorização explícita do usuário para exceção à margem de uso.
- Reproduzida exceção WPF em `DriverInfo.HasProblem` com checkbox/binding padrão; corrigido para OneWay. Causa exata do crash originalmente relatado em Diagnósticos não confirmada por logs; mitigadas consultas presas, cancelamento bloqueante e cache de falhas.
- Consultas: timeout30s, cancelamento não bloqueante, retry, resultados tardios descartados, dados anteriores preservados, limite2 backends pendentes por módulo e exclusão entre consultas/ações. Sem mudanças em segurança de instalação de drivers nem contratos Core.
- Tema: recursos semânticos, controles/tabelas/combos/GroupBox escuros, estados de botões, bindings readonly, versão do assembly e título não interativo. Erro global inesperado agora encerra com aviso, em vez de continuar indiscriminadamente.
- Validação: restore sem warnings; 108 testes existentes +33 novos =141 aprovados. Inclui duas consultas reais de leitura no Windows, sem reparos nem alterações de drivers. Cinco testes de render/binding repetidos após checar limites de botões também passaram.
- Evidências/limitações: `docs/ESTABILIDADE-1.2.1.md`; TRX em `artifacts/test-results`; imagens sintéticas do XAML real em `artifacts/ui-validation-final` (não são captura manual do app instalado).
- Publicação autocontida win-x64 em `artifacts/release-1.2.1/portable`; dois instaladores, ZIP e SHA256SUMS em `artifacts/release-1.2.1/github-assets`. Executável1.2.1.0. Quatro arquivos publicados no GitHub e R2; site atualizado para1.2.1.
- Instalações encontradas: usuário1.1.0 e máquina1.1.0. Inspeção restrita não retornava processos; fora da restrição confirmou PID32676 em `C:\Program Files\TechHub\TechHub.App.exe`, respondendo. Este é o app antigo aberto. Não removidas instalações/dados.
- Instalação por usuário1.2.1 concluída após usuário confirmar trabalho salvo/janela fechada. Restava PID32676 da1.1.0 sem janela (handle0); encerrado especificamente após verificar caminho/identidade. Investigar encerramento residual como pendência, sem afirmar que esta versão já o corrige.
- Backup privado feito em `artifacts/local-user-backup-before-1.2.1/Data`; instalador validado por SHA256, saída0, sem reinício. Banco e arquivos de configuração comparados por hash antes/depois: preservados. Nunca incluir backup local na publicação.
- Versão instalada confirmada1.2.1.0 em `%LocalAppData%\TechHub\TechHub.App.exe`; aberta (PID14152), janela respondendo e log de inicialização1.2.1 às17:35. Atalho `C:\Users\marco\OneDrive\Desktop\TechHub (Usuário).lnk` aponta para esse executável. Instalação antiga de máquina permanece; não removida.
- Próximo: validação local da1.2.1, depois etapaD (modalidade, elevação, atualizador com integridade/autenticidade e preservação de dados). A/B/C ainda têm validações interativas pendentes; E/F/G/H não concluídas por este lote. Qwen não utilizado nesta rodada.
- Duas delegações Sol/medium para XAML e consultas concluídas/revisadas; nenhuma delegação ativa. Houve interrupção por quota e retomada após reset natural, sem comprar créditos/resgatar resets. Conferir limite antes da próxima etapa pesada.
- Limite na conclusão desta instalação: primário94% usado, semanal96% usado (4% restante). Margem preventiva do usuário atingida: próxima etapa pesada/publicação adiada; continuidade salva, sem créditos/resets utilizados.
- Domínio canônico de produção: `https://techhub-downloads.pages.dev/`. URLs com hash são snapshots de deploy, não o endereço estável. R2 já habilitado no histórico posterior; notas antigas abaixo não refletem o estado atual.
- Exceção à margem5% autorizada pelo usuário com “autorizo”, especificamente para concluir esta publicação. Sem compra de créditos/resgate de resets; a regra preventiva permanece para etapas futuras.
- Publicação concluída: fonte commit `d876509` enviado a main; release ID413195535/tagv1.2.1 pública e confirmada como latest. Link: `https://github.com/marcoszhp/TechHub/releases/tag/v1.2.1`.
- R2: quatro objetos em `techhub-downloads/releases/1.2.1/`; todos baixados integralmente em fluxo e comparados por SHA256 com os arquivos locais. Digests/tamanhos dos quatro assets GitHub também validados antes da publicação.
- Pages: deploy `https://e4e32795.techhub-downloads.pages.dev`; domínio canônico `https://techhub-downloads.pages.dev/` conferido HTTP200, versão1.2.1 e quatro links atualizados. Backup privado, Data e logs não enviados.
- Script de release agora usa versão parametrizada, quatro assets, hashes locais/remotos e ReleaseId opcional. A lista de releases teve encapsulamento de array corrigido; rascunho existente foi retomado pelo ID, sem duplicação.

## Histórico anterior (não usar como estado atual)

### Atualização integrada 1.2.0 (2026-10-14)

- Implementado o fluxo de atualização no Dashboard: consulta a release mais recente do GitHub, compara a versão instalada, baixa o instalador sem administrador pelo R2 e abre o instalador sob comando do usuário.
- Versão do aplicativo e dos instaladores atualizada para `1.2.0`; os artefatos foram compilados e os checksums gerados.
- Publicação em R2 preparada em `releases/1.2.0/`; a página `downloads/index.html` aponta para os novos arquivos.
- Release GitHub `v1.2.0` publicada com os quatro artefatos; commit `b361262` e tag `v1.2.0` enviados.
- Cloudflare Pages atualizado em `https://21ab5b22.techhub-downloads.pages.dev`; domínio estável é `https://techhub-downloads.pages.dev/` (URLs com hash são snapshots).

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
