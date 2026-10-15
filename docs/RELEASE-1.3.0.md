# TechHub 1.3.0 — atualização pelo aplicativo

## Atualização integrada

Nas instalações padrão Windows x64, por usuário ou por máquina, o Dashboard pode verificar a versão e iniciar a atualização. Abra **Dashboard > Verificar atualização > Atualizar e reiniciar**. Execute o TechHub sem privilégios de administrador; a instalação por máquina solicita UAC somente durante a instalação.

Quem ainda está na versão 1.2.1 precisa instalar a 1.3.0 uma vez pelo instalador para receber o atualizador integrado. Depois dessa migração inicial, as atualizações podem ser iniciadas pelo Dashboard.

Instalações portáteis ou em diretórios personalizados não são atendidas pelo atualizador integrado e devem ser atualizadas manualmente. A atualização cria backup de SQLite e JSON, sem logs ou anexos. O arquivo baixado tem tamanho e SHA-256 verificados; isso não equivale a assinatura de código com certificado. Não há rollback automático.

## Evidências e limites

- Testes automatizados cobrem integridade, download, cancelamento, falhas, concorrência e interface escura.
- Um teste com Inno real e instalação isolada atualizou executáveis de teste da 1.2.1 para a 1.3.0, preservou banco/configurações e reiniciou no mesmo usuário sem elevação. A consulta GitHub e a política do ponto de entrada foram substituídas somente nesse teste.
- O aplicativo WPF real, com dados isolados, inicializou e encerrou normalmente tanto pelo fechamento da janela quanto pelo caminho usado na atualização, sem processo residual. A consulta oficial ao GitHub também foi validada separadamente, incluindo rejeição de hash alterado.
- O fluxo completo com duas contas Windows/RDS ou autorização UAC de outra conta ainda não foi exercitado. Existem proteções entre sessões, mas esse cenário exige validação adicional. A interface de confirmação e a passagem completa ao auxiliar não foram exercitadas por clique no aplicativo instalado.

Atualizações periódicas, instalação automática e garantia de rollback ou robustez universal não fazem parte do escopo descrito aqui.
