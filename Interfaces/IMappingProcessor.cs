using ReviewTool.Models;

namespace ReviewTool.Interfaces;

internal interface IMappingProcessor
{
    Task<bool> TryPerformMappingToAsync(string originalImagesFolderPath,
                                        string reviewFolderPath,
                                        IReadOnlyList<ImageFileMappingInfo> mappingInfo,
                                        CancellationTokenSource mappingCts);



}
