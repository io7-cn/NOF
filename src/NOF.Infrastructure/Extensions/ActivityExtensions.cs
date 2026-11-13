using NOF.Hosting;
using NOF.Infrastructure;

namespace System.Diagnostics;

public static class ActivityExtensions
{
    extension(Activity activity)
    {
        public void SetServiceDeploymentTags(DeploymentOptions deployment)
        {
            ArgumentNullException.ThrowIfNull(activity);
            ArgumentNullException.ThrowIfNull(deployment);

            activity.SetTag(NOFInfrastructureConstants.Deployment.Tags.ServiceId, deployment.ServiceId);
            activity.SetTag(NOFInfrastructureConstants.Deployment.Tags.ServiceName, deployment.ServiceName);
            activity.SetTag(NOFInfrastructureConstants.Deployment.Tags.InstanceId, deployment.InstanceId);
        }

        public string ToTraceParent()
        {
            ArgumentNullException.ThrowIfNull(activity);

            return $"00-{activity.TraceId}-{activity.SpanId}-{(byte)activity.ActivityTraceFlags:x2}";
        }
    }

    extension(ActivitySource source)
    {
        public Activity? StartActivityWithParent(string name, ActivityKind kind, string? traceParent, DeploymentOptions? deployment = null)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (!string.IsNullOrWhiteSpace(traceParent) &&
                ActivityContext.TryParse(traceParent, null, out var parentContext))
            {
                var activity = source.StartActivity(name, kind, parentContext);
                if (deployment is not null)
                {
                    activity?.SetServiceDeploymentTags(deployment);
                }

                return activity;
            }

            var randomParent = new ActivityContext(
                ActivityTraceId.CreateRandom(),
                ActivitySpanId.CreateRandom(),
                ActivityTraceFlags.Recorded,
                isRemote: true);
            var startedActivity = source.StartActivity(name, kind, randomParent);
            if (deployment is not null)
            {
                startedActivity?.SetServiceDeploymentTags(deployment);
            }

            return startedActivity;
        }

    }
}
