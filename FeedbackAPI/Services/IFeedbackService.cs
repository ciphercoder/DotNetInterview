using FeedbackAPI.DTOs;
using FeedbackAPI.Models;

namespace FeedbackAPI.Services;

public interface IFeedbackService
{
    Task<PagedResult<FeedbackResponseDto>> GetAllAsync(FeedbackFilterParams filter);
    Task<FeedbackResponseDto?> GetByIdAsync(int id);
    Task<FeedbackResponseDto> CreateAsync(FeedbackCreateDto dto);
    Task<FeedbackResponseDto?> UpdateAsync(int id, FeedbackUpdateDto dto);
    Task<bool> DeleteAsync(int id);
    Task<FeedbackSummaryDto> GetSummaryAsync(FeedbackFilterParams filter);
    Task<IEnumerable<TrendDataPointDto>> GetTrendsAsync(DateTime from, DateTime to, string groupBy);
}
