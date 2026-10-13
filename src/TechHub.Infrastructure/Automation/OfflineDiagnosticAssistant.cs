using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Automation;

public sealed class OfflineDiagnosticAssistant : IDiagnosticAssistant
{
    private static readonly (string[] Terms, AssistantResponse Response)[] Rules =
    [
        (["lento", "travando", "devagar"], new AssistantResponse("desempenho", "O sintoma sugere pressão de recursos ou excesso de inicialização.", ["O problema acontece logo após ligar?", "O disco fica em 100%?"], ["Atualize o Dashboard", "Analise a prévia de Limpeza no modo Seguro", "Revise os eventos críticos"], ["Feche aplicativos não utilizados", "Não desative serviços sem identificar o impacto."])),
        (["internet", "wifi", "wi-fi", "rede"], new AssistantResponse("conectividade", "Vamos separar falha de Wi-Fi, DNS, gateway e serviço remoto.", ["Outros dispositivos navegam?", "O ícone de rede mostra erro?"], ["Execute ping e a descoberta consentida da Rede", "Compare DNS e gateway", "Verifique se o adaptador aparece no inventário de Drivers"], ["Reinicie o roteador somente se isso estiver autorizado", "Não redefina a pilha de rede sem salvar o contexto."])),
        (["impressora", "imprime", "imprimir"], new AssistantResponse("impressão", "O problema pode estar na fila, porta, driver ou disponibilidade da impressora.", ["A impressora aparece como pronta?", "A página de teste falha em todos os computadores?"], ["Atualize o inventário de Impressoras", "Valide IP/porta e driver", "Confira a fila antes de reinstalar"], ["Não remova drivers compartilhados sem confirmação."])),
        (["segurança", "vírus", "defender", "bitlocker"], new AssistantResponse("segurança", "A coleta local pode mostrar controles desativados sem enviar dados para fora.", ["Você reconhece o alerta?", "Há backup dos dados importantes?"], ["Atualize a tela Segurança", "Solicite uma varredura rápida do Defender", "Revise os alertas antes de alterar políticas"], ["Não desative Defender ou firewall para testar."]))
    ];

    public Task<AssistantResponse> AskAsync(string symptom, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symptom);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = symptom.Trim().ToLowerInvariant();
        var match = Rules.FirstOrDefault(rule => rule.Terms.Any(normalized.Contains));
        return Task.FromResult(match.Response ?? new AssistantResponse(symptom, "Não encontrei uma regra específica; começaremos por coleta segura.", ["Quando começou?", "O problema se repete?"], ["Atualize o Dashboard", "Colete Diagnóstico", "Evite reparos destrutivos antes da prévia"], ["Faça backup antes de mudanças importantes."]));
    }
}
