using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OrchardCore.Workflows.Abstractions.Models;
using OrchardCore.Workflows.Models;

namespace OrchardCore.Workflows.Activities
{
    public class JoinTask : TaskActivity
    {
        public enum JoinMode
        {
            WaitAll,
            WaitAny
        }

        public JoinTask(IStringLocalizer<JoinTask> localizer)
        {
            T = localizer;
        }

        private IStringLocalizer T { get; }

        public override string Name => nameof(JoinTask);
        public override LocalizedString Category => T["Control Flow"];

        public JoinMode Mode
        {
            get => GetProperty(() => JoinMode.WaitAll);
            set => SetProperty(value);
        }

        private IList<string> Branches
        {
            get => GetProperty(() => new List<string>());
            set => SetProperty(value);
        }

        public override IEnumerable<Outcome> GetPossibleOutcomes(WorkflowExecutionContext workflowContext, ActivityContext activityContext)
        {
            return Outcomes(T["Joined"]);
        }

        public override ActivityExecutionResult Execute(WorkflowExecutionContext workflowContext, ActivityContext activityContext)
        {
            // Wait for all incoming branches to have executed their activity.
            var branches = Branches;
            var inboundTransitions = workflowContext.GetInboundTransitions(activityContext.ActivityRecord.Id);
            var done = false;

            switch (Mode)
            {
                case JoinMode.WaitAll:
                    done = inboundTransitions.All(x => branches.Contains(GetTransitionKey(x)));
                    break;
                case JoinMode.WaitAny:
                    done = inboundTransitions.Any(x => branches.Contains(GetTransitionKey(x)));

                    if (done)
                    {
                        // Remove any inbound blocking activities.
                        var ancestorActivityIds = workflowContext.GetInboundActivityPath(activityContext.ActivityRecord.Id).ToList();
                        var blockingActivities = workflowContext.WorkflowInstance.AwaitingActivities.Where(x => ancestorActivityIds.Contains(x.ActivityId)).ToList();

                        foreach (var blockingActivity in blockingActivities)
                        {
                            workflowContext.WorkflowInstance.AwaitingActivities.Remove(blockingActivity);
                        }
                    }
                    break;
            }

            if (done)
            {
                return Outcomes("Joined");
            }

            return Noop();
        }
        public override Task OnActivityExecutedAsync(WorkflowExecutionContext workflowContext, ActivityContext activityContext)
        {
            // Get outbound transitions of the executing activity.
            var outboundTransitions = workflowContext.GetOutboundTransitions(activityContext.ActivityRecord.Id);

            // Get any transition that is pointing to this activity.
            var inboundTransitionsQuery =
                from transition in outboundTransitions
                let destinationActivity = workflowContext.GetActivity(transition.DestinationActivityId)
                where destinationActivity.Activity.Name == Name
                select transition;

            var inboundTransitions = inboundTransitionsQuery.ToList();

            foreach (var inboundTransition in inboundTransitions)
            {
                var mergeActivity = (JoinTask)workflowContext.GetActivity(inboundTransition.DestinationActivityId).Activity;
                var branches = mergeActivity.Branches;
                mergeActivity.Branches = branches.Union(new[] { GetTransitionKey(inboundTransition) }).Distinct().ToList();
            }

            return Task.CompletedTask;
        }

        private string GetTransitionKey(TransitionRecord transition)
        {
            var sourceActivityId = transition.SourceActivityId;
            var sourceOutcomeName = transition.SourceOutcomeName;

            return $"@{sourceActivityId}_{sourceOutcomeName}";
        }
    }
}