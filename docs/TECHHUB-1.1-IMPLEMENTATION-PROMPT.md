# TechHub 1.1 — contrato de execução

## Objetivo autorizado

Transformar o aplicativo Windows existente em uma central de suporte funcional,
inspirada na foto fornecida pelo proprietário. Preservar C#/.NET 8, WPF, MVVM,
SQLite, o fluxo de confirmação e auditoria e os dados existentes. Não substituir
o aplicativo por uma demonstração web.

## Direção de interface

Barra de título nativa arrastável; menu lateral azul-escuro; área de trabalho
clara; navegação permanente; mapa de rede sobre grade escura; detalhes do
dispositivo ao lado. Hierarquia legível, estados de carregamento/erro, foco de
teclado e rolagem quando a janela for pequena. A foto é referência de estrutura,
não especificação de funcionalidades ilegíveis.

## Etapas e aceite

1. **Base:** inventariar o código, preservar alterações locais e registrar decisões.
2. **Shell:** corrigir o menu, título e redimensionamento; carregar módulos sob
   demanda para que uma consulta lenta não bloqueie a abertura.
3. **Rede:** adaptador e endereço locais reais, descoberta IPv4 limitada à rede
   autorizada, consentimento, progresso e cancelamento; mapa lógico com seleção,
   lista acessível, IP/MAC/nome/latência quando disponíveis e exportação CSV.
   Distinguir conexões inferidas de topologia física e cache ARP de host ativo.
4. **Central técnica:** informações do PC, ferramentas Windows permitidas,
   diagnóstico de rede seguro, clientes/equipamentos locais, histórico auditado,
   backup dos dados e restauração em destino vazio; configurações úteis.
5. **Integração:** corrigir campos ausentes e layouts dos módulos existentes.
   Não executar reparos, limpeza, instalação de drivers ou upgrades no computador
   do proprietário para testar. Não alegar sucesso em módulos não validados.
6. **Verificação:** compilar, executar testes automatizados, inspecionar a janela
   real, navegação e arraste; revisar independentemente os fluxos de maior risco.
7. **Distribuição:** gerar instalador e ZIP portátil limpos, versão e SHA-256.
   O instalador usa LocalAppData; somente o ZIP portátil contém portable.flag.
   Publicar no GitHub autorizado e página de download no Cloudflare se o acesso
   permitir. Não contratar serviços nem habilitar cobrança sem decisão específica.
   Informar com precisão onde os binários ficam hospedados.

## Segurança e limites

- Não inventar dispositivos, medições, testes, links ou implantação.
- Não publicar dados locais, backups, bancos, credenciais ou logs do proprietário.
- Consentimento antes de descoberta; ações mutáveis continuam no ActionRunner.
- Backup não deve substituir silenciosamente dados ativos; validar caminhos ZIP.
- Não ampliar o agente remoto mutável: continua bloqueado sem infraestrutura TLS.
- Alterações reversíveis, revisão final do coordenador e cache atualizado.
- Se uma janela de uso tiver 5% ou menos restante, registrar continuidade e adiar
  a próxima etapa pesada, sem comprar créditos ou resgatar resets.

## Evidências exigidas na entrega

Arquivos alterados, resultados reais de build/testes, captura da interface,
limitações conhecidas, nomes/hashes dos pacotes e URLs verificadas. Um plano,
um build ou uma captura bonita, isoladamente, não concluem este contrato.
