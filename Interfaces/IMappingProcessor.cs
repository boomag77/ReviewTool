using ReviewTool.Models;

namespace ReviewTool.Interfaces;

internal interface IMappingProcessor
{
    Task<bool> TryPerformMappingToAsync(string originalImagesFolderPath,
                                        IReadOnlyList<ImageFileMappingInfo> mappingInfo,
                                        CancellationTokenSource mappingCts,
                                        bool isOcsEnabled);

    
}
