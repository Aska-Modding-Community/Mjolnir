using System;
using Il2CppInterop.Runtime.Injection;
using SSSGame.AI.FSM;

namespace Mjolnir.SDK.AI;

/// <summary>
/// A step of a job's state machine, injected into IL2CPP: it starts its step as its state is entered and moves the
/// villager on once the step is over (<see cref="JobRun"/>). One per state; which job and step it is, is the SDK's table.
/// </summary>
internal sealed class JobAction : vStateAction
{
    public JobAction(IntPtr ptr) : base(ptr) { }

    public JobAction() : base(ClassInjector.DerivedConstructorPointer<JobAction>()) => ClassInjector.DerivedConstructorBody(this);

    public override void OnStateEnter(IFSMBehaviourController fsmBehaviour) => JobQuests.Enter(this, fsmBehaviour);

    public override void OnStateUpdate(IFSMBehaviourController fsmBehaviour) => JobQuests.Update(this, fsmBehaviour);

    public override bool IsBusy(IFSMBehaviourController fsmBehaviour) => false;
}
