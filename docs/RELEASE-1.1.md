# TechHub 1.1

Central de manutenção e suporte para Windows x64, com dados locais.

## Novidades

- Navegação lateral escura, área de trabalho clara e barra de título arrastável.
- Carregamento de módulos sob demanda, sem consultar todos os serviços ao abrir.
- Mapa lógico de rede, seleção de adaptador, consentimento, cancelamento,
  detalhes por dispositivo, lista equivalente e exportação CSV.
- Informações do computador, ferramentas Windows e console de diagnóstico limitado.
- Cadastro local de clientes/equipamentos, histórico e backup do banco/configurações.
- Correções de campos de drivers, impressoras e apresentação de dados indisponíveis.

## Instalação

Use o instalador para gravar dados em `%LocalAppData%\TechHub`. A versão portátil
deve ser extraída em uma pasta gravável e usa a subpasta `Data`. O runtime .NET
está incluído. Confira os arquivos com `SHA256SUMS.txt`.

## Limitações e segurança

- Pacotes sem certificado de assinatura digital. Verifique origem e integridade.
- O mapa não identifica cabeamento físico. Ausência de resposta ICMP não significa
  equipamento desligado; cache ARP, DNS e fabricante OUI não comprovam identidade.
- Backup inclui o banco SQLite e configurações JSON, não anexos, relatórios ou logs.
  A restauração cria uma cópia em pasta vazia e não troca o banco ativo.
- Reparos do sistema, instalação de drivers/impressoras e atualizações requerem
  permissões e confirmação. Não foram executados destrutivamente no computador do
  proprietário durante a verificação.
- O agente remoto mutável permanece bloqueado sem a infraestrutura TLS exigida.
- A página de download fica no Cloudflare Pages; binários no GitHub Releases.
