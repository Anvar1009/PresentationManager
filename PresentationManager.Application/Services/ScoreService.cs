using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Services;

/// <summary>Judge scoring (via the Telegram bot) and the resulting final-scores rollup (via the Admin/
/// SuperAdmin panels).</summary>
public sealed class ScoreService
{
    private readonly IScoreRepository _scoreRepository;
    private readonly ICriterionRepository _criterionRepository;
    private readonly IPresentationRepository _presentationRepository;

    public ScoreService(
        IScoreRepository scoreRepository, ICriterionRepository criterionRepository, IPresentationRepository presentationRepository)
    {
        _scoreRepository = scoreRepository;
        _criterionRepository = criterionRepository;
        _presentationRepository = presentationRepository;
    }

    public Task<List<Score>> GetAllAsync(CancellationToken ct = default) => _scoreRepository.GetAllAsync(ct);

    /// <summary>A judge revising their own earlier score for the same presentation/criterion is expected
    /// (upsert), not an error — see <see cref="IScoreRepository.UpsertAsync"/>.</summary>
    public async Task UpsertAsync(int presentationId, int judgeId, int criterionId, int value, CancellationToken ct = default)
    {
        var criteria = await _criterionRepository.GetAllAsync(ct);
        var criterion = criteria.FirstOrDefault(c => c.Id == criterionId)
            ?? throw new InvalidOperationException("Mezon topilmadi.");

        if (value < 0 || value > criterion.MaxScore)
        {
            throw new InvalidOperationException($"Ball 0 dan {criterion.MaxScore} gacha bo'lishi kerak.");
        }

        await _scoreRepository.UpsertAsync(presentationId, judgeId, criterionId, value, ct);
    }

    /// <summary>This judge's own scores for one presentation, keyed by criterion — used by the bot to show
    /// "Mazmun (7/10)" against each criterion button instead of a blank list.</summary>
    public async Task<Dictionary<int, int>> GetJudgeProgressAsync(int presentationId, int judgeId, CancellationToken ct = default)
    {
        var scores = await _scoreRepository.GetByPresentationAndJudgeAsync(presentationId, judgeId, ct);
        return scores.ToDictionary(s => s.CriterionId, s => s.Value);
    }

    public async Task<List<PresentationScoreSummary>> GetFinalScoresAsync(int projectId, CancellationToken ct = default)
    {
        var presentations = await _presentationRepository.GetAllOrderedAsync(projectId, ct);
        var criteria = await _criterionRepository.GetByProjectIdAsync(projectId, ct);
        var presentationIds = presentations.Select(p => p.Id).ToList();

        List<Score> scores = presentationIds.Count == 0
            ? new List<Score>()
            : await _scoreRepository.GetByPresentationIdsAsync(presentationIds, ct);

        var result = new List<PresentationScoreSummary>();
        foreach (var presentation in presentations)
        {
            var averages = new Dictionary<int, double>();
            foreach (var criterion in criteria)
            {
                var values = scores
                    .Where(s => s.PresentationId == presentation.Id && s.CriterionId == criterion.Id)
                    .Select(s => (double)s.Value)
                    .ToList();
                averages[criterion.Id] = values.Count == 0 ? 0 : values.Average();
            }

            result.Add(new PresentationScoreSummary
            {
                PresentationId = presentation.Id,
                PresenterFullName = presentation.FullName,
                Title = presentation.Title,
                AverageByCriterionId = averages,
                Total = averages.Values.Sum()
            });
        }

        return result;
    }
}
