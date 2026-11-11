using NOF.Contract;

namespace AntDesign;

public static class NOFSampleWebUIExtensions
{
    extension(IMessageService messageService)
    {
        /// <summary>Displays the selected outcome's message.</summary>
        public void ShowMessage(NOF.Contract.Result result, string? successMessage = null, bool showSuccess = false)
        {
            switch (result)
            {
                case Success success:
                    if (showSuccess)
                    {
                        messageService.Success(successMessage ?? success.Message);
                    }
                    break;
                case Failure failure:
                    messageService.Error(failure.Message);
                    break;
                default:
                    throw new InvalidOperationException("The response contains no outcome case.");
            }
        }

        /// <summary>Displays the outcome message and returns successful data.</summary>
        public T? UnwrapWithMessage<T>(Result<T> result, string? successMessage = null, bool showSuccess = false)
        {
            switch (result)
            {
                case Success<T> success:
                    if (showSuccess)
                    {
                        messageService.Success(successMessage ?? success.Message);
                    }
                    return success.Data;
                case Failure failure:
                    messageService.Error(failure.Message);
                    return default;
                default:
                    throw new InvalidOperationException("The response contains no outcome case.");
            }
        }

        /// <summary>Builds a message from successful data.</summary>
        public T? UnwrapWithMessage<T>(Result<T> result, Func<T, string> successMessageFactory, bool showSuccess = false)
        {
            ArgumentNullException.ThrowIfNull(successMessageFactory);
            return messageService.UnwrapWithMessage(result,
                result is Success<T> success ? successMessageFactory(success.Data) : null, showSuccess);
        }
    }
}
