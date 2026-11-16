using System.ComponentModel;

namespace NOF.Infrastructure;

public enum OutboxMessageStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}
