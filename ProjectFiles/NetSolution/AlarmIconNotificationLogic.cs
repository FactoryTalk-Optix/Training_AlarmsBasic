#region Using directives
using System;
using CoreBase = FTOptix.CoreBase;
using FTOptix.HMIProject;
using UAManagedCore;
using System.Linq;
using UAManagedCore.Logging;
using FTOptix.NetLogic;
#endregion

public class AlarmIconNotificationLogic : BaseNetLogic
{
	public override void Start()
	{
		var model = Project.Current.Get("Model");
		if(model == null)
		{
			Log.Error("AlarmIconNotificationLogic", "Could not get Model folder of Alarm Notification Logic");
		}

		var context = model.Context;
		affinityId = context.AssignAffinityId();

		RegisterObserverOnLocalizedAlarmsContainer(context);
		RegisterObserverOnSessionLocaleIdChanged(context);
		RegisterObserverOnLocalizedAlarmsObject(context);
	}

	public override void Stop()
	{
		if (alarmEventRegistration != null)
			alarmEventRegistration.Dispose();
		if (alarmEventRegistration2 != null)
			alarmEventRegistration2.Dispose();

		alarmEventRegistration = null;
		alarmEventRegistration2 = null;
		alarmsNotificationObserver = null;
		retainedAlarmsObjectObserver = null;
	}

	public void RegisterObserverOnLocalizedAlarmsObject(IContext context)
	{
		var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);

		retainedAlarmsObjectObserver = new RetainedAlarmsObjectObserver((ctx) => RegisterObserverOnLocalizedAlarmsContainer(ctx));

		// observe ReferenceAdded of localized alarm containers
		alarmEventRegistration2 = retainedAlarms.RegisterEventObserver(
			retainedAlarmsObjectObserver, EventType.ForwardReferenceAdded, affinityId);
	}

	public void RegisterObserverOnLocalizedAlarmsContainer(IContext context)
	{
		var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
		var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
		var localizedAlarmsContainer = context.GetNode((NodeId)localizedAlarmsVariable.GetValue());

		if (alarmEventRegistration != null)
		{
			alarmEventRegistration.Dispose();
			alarmEventRegistration = null;
		}

		alarmsNotificationObserver = new AlarmsNotificationObserver(LogicObject);
		alarmsNotificationObserver.Initialize();

		alarmEventRegistration = localizedAlarmsContainer.RegisterEventObserver(
			alarmsNotificationObserver,
			EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved, affinityId);
	}

	public void RegisterObserverOnSessionLocaleIdChanged(IContext context)
	{
		var currentSessionLocaleIds = context.Sessions.CurrentSessionInfo.SessionObject.Children["ActualLocaleId"];

		localeIdChangedObserver = new CallbackVariableChangeObserver((IUAVariable variable, UAValue newValue, UAValue oldValue, uint[] a, ulong aa) =>
		{
			RegisterObserverOnLocalizedAlarmsContainer(context);
		});

		localeIdsRegistration = currentSessionLocaleIds.RegisterEventObserver(
			localeIdChangedObserver, EventType.VariableValueChanged, affinityId);
	}

	private class RetainedAlarmsObjectObserver : IReferenceObserver
	{
		public RetainedAlarmsObjectObserver(Action<IContext> action)
		{
			registrationCallback = action;
		}

		public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			string localeId = "en-US";

			var localeIds = targetNode.Context.Sessions.CurrentSessionHandler.ActualLocaleId;
			if (!String.IsNullOrEmpty(localeId))
				localeId = localeIds;

			targetNode.Context.Sessions.CurrentSessionHandler.ActualLocaleId.First();

			if (targetNode.BrowseName == localeId)
				registrationCallback(targetNode.Context);
		}

		public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
		}

		private Action<IContext> registrationCallback;
	}

	private class AlarmsNotificationObserver : IReferenceObserver
	{
		public AlarmsNotificationObserver(IUANode logicNode)
		{
			this.logicNode = logicNode;
		}

		public void Initialize()
		{
			retainedAlarmsCount = logicNode.GetVariable("AlarmCount");
			lastAlarm = logicNode.GetVariable("LastAlarm");

			IContext context = logicNode.Context;
			var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
			var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
			var localizedAlarmsNodeId = (NodeId)localizedAlarmsVariable.Value;
			IUANode localizedAlarmsContainer = null;
			if (localizedAlarmsNodeId != null && !localizedAlarmsNodeId.IsEmpty)
				localizedAlarmsContainer = context.GetNode(localizedAlarmsNodeId);

			retainedAlarmsCount.Value = localizedAlarmsContainer?.Children.Count ?? 0;
			if (retainedAlarmsCount.Value > 0)
			{
				lastAlarm.Value = localizedAlarmsContainer.Children.Last().NodeId;
			}
			else
			{
				lastAlarm.Value = NodeId.Empty;
			}
		}

		public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			++retainedAlarmsCount.Value;

			lastAlarm.Value = targetNode.NodeId;
		}

		public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			if (retainedAlarmsCount.Value > 0)
				--retainedAlarmsCount.Value;

			IContext context = logicNode.Context;
			var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
			var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
			var localizedAlarmsNodeId = (NodeId)localizedAlarmsVariable.Value;
			IUANode localizedAlarmsContainer = null;
			if (localizedAlarmsNodeId != null && !localizedAlarmsNodeId.IsEmpty)
				localizedAlarmsContainer = context.GetNode(localizedAlarmsNodeId);

			if (retainedAlarmsCount.Value == 0 || localizedAlarmsContainer == null)
			{
				lastAlarm.Value = NodeId.Empty;
			}
			else
			{
				lastAlarm.Value = localizedAlarmsContainer.Children.Last().NodeId;
			}
		}

		private IUAVariable retainedAlarmsCount;
		private IUAVariable lastAlarm;
		private IUANode logicNode;
	}

	private uint affinityId = 0;
	private AlarmsNotificationObserver alarmsNotificationObserver;
	private RetainedAlarmsObjectObserver retainedAlarmsObjectObserver;
	private IEventRegistration alarmEventRegistration;
	private IEventRegistration alarmEventRegistration2;
	private IEventRegistration localeIdsRegistration;
	private IEventObserver localeIdChangedObserver;
}
