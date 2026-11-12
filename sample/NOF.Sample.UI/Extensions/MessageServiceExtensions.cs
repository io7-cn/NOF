using NOF.Abstraction;
using NOF.Contract;

namespace AntDesign;

public static class NOFSampleWebUIExtensions
{
    extension(IMessageService messageService)
    {
        /// <summary>Displays a payload-free result's selected message.</summary>
        public void ShowMessage(NOF.Contract.Result result, string? successMessage = null, bool showSuccess = false)
        {
            switch (result)
            {
                case NOF.Contract.Empty:
                    if (showSuccess && successMessage is not null)
                    {
                        messageService.Success(successMessage);
                    }
                    break;
                case Failure failure:
                    messageService.Error(failure.Message);
                    break;
                case null:
                    throw new InvalidOperationException("The response contains no outcome case.");
            }
        }

        /// <summary>Displays an error or caller-provided success message and returns the successful value.</summary>
        public T? UnwrapWithMessage<T>(Result<T> result, string? successMessage = null, bool showSuccess = false)
        {
            switch (result)
            {
                case Failure failure:
                    messageService.Error(failure.Message);
                    return default;
                case T value:
                    if (showSuccess && successMessage is not null)
                    {
                        messageService.Success(successMessage);
                    }
                    return value;
                case null:
                    throw new InvalidOperationException("The response contains no outcome case.");
            }
        }

        /// <summary>Builds a message from a successful value.</summary>
        public T? UnwrapWithMessage<T>(Result<T> result, Func<T, string> successMessageFactory, bool showSuccess = false)
        {
            ArgumentNullException.ThrowIfNull(successMessageFactory);
            return messageService.UnwrapWithMessage(result,
                result is T value ? successMessageFactory(value) : null, showSuccess);
        }
    }
}
