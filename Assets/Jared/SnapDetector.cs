using System.Collections;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using UnityEngine;

public class SnapDetector : MonoBehaviour
{
    [SerializeField]
    private Handedness trackedHandedness = Handedness.Right;

    [SerializeField]
    private TrackedHandJoint trackedJoint = TrackedHandJoint.Palm;

    private TrackedHandJoint thumbBase = TrackedHandJoint.ThumbMetacarpalJoint;
    private TrackedHandJoint thumbTip = TrackedHandJoint.ThumbTip;
    private TrackedHandJoint middleTip = TrackedHandJoint.MiddleTip;

    private PoseHistory thumbBaseJoint;
    private PoseHistory thumbTipJoint;
    private PoseHistory middleTipJoint;

    private const int VELOCITY_FRAMES = 5;

    [SerializeField] private float snapReadyDistanceThreshold = 0.03f;
    [SerializeField] private float snappingVelocityThreshold = 0.05f;
    [SerializeField] private float snapCompletedDistanceThreshold = 0.03f;

    // Here's how I think the state progression is going to work.
    // We start out by default NotSnapping.
    // When the distance between thumb tip and middle tip goes under a certain threshold (say 2cm), we enter SnapReady.
    // While in SnapReady, we actively watch for snapping behavior. Snap behavior includes:
    // - Large relative velocity of middle and thumb tip;
    // - Increasing distance between middle and thumb tip;
    // - Decreasing distance between thumb base and middle tip.
    // If we detect any of these metrics, within some threshold(s), we enter Snapping state. In Snapping,
    // - Once middle tip to thumb base distance and relative velocity are reduced to within some threshold, we 
    //    register a snap.
    // - If more than say 0.3 seconds pass without confirming a snap, exit to NotSnapping or SnapReady as appropriate.
    
    public enum SnapState
    {
        NotInitialized,
        NotSnapping,
        SnapReady,
        Snapping
        // No SnapCompleted
    }

    public SnapState snapState = SnapState.NotInitialized;

    public Renderer indicatorRenderer;
    
    //private List<Vector3> 

    private class PoseHistory
    {
        private Handedness handedness = Handedness.Right;
        private TrackedHandJoint joint;
        private Queue<Vector3> positions = new Queue<Vector3>();
        private Queue<Quaternion> rotations = new Queue<Quaternion>();
        private Queue<float> timestamps = new Queue<float>();

        public PoseHistory(Handedness trackedHandedness, TrackedHandJoint trackedJoint)
        {
            this.handedness = trackedHandedness;
            this.joint = trackedJoint;
        }
        
        public void DoUpdate()
        {
            // Get hand and joint, bailing if we can't find either
            IMixedRealityHand hand = GetController(handedness) as IMixedRealityHand;
            if (hand == null || !hand.TryGetJoint(joint, out MixedRealityPose pose))
            {
                //SetChildrenActive(false);
                return;
            }
            // Yeet the oldest set of data if we're at our max
            if (positions.Count == VELOCITY_FRAMES)
            {
                positions.Dequeue();
                rotations.Dequeue();
                timestamps.Dequeue();
            }
            // Add new data
            positions.Enqueue(pose.Position);
            rotations.Enqueue(pose.Rotation);
            timestamps.Enqueue(Time.time);
        }

        public Vector3 AverageVelocity()
        {
            Vector3 avg = Vector3.zero;
            // If we can't compute velocity yet due to lack of data, return nothing
            if (timestamps.Count == 0)
            {
                Debug.LogWarning("PoseHistory was asked for average velocity with zero or one recorded poses.");
                return avg;
            }

            Vector3[] arrayPositions = positions.ToArray();
            float[] arrayTimes = timestamps.ToArray();
            
            for (int i = 1; i < positions.Count; i++)
            {
                avg += (1 / (float) positions.Count) 
                       * (arrayPositions[i] - arrayPositions[i - 1]) 
                       / (arrayTimes[i] - arrayTimes[i - 1]);
            }
            

            return avg;
        }

        public static float CurrentDistance(PoseHistory joint1, PoseHistory joint2)
        {
            return Vector3.Distance(joint1.positions.Peek(), joint2.positions.Peek());
        }
        
        public static Vector3 AverageInterJointVelocity(PoseHistory joint1, PoseHistory joint2)
        {
            // TODO error checking in case the pose histories have different numbers of records
            Vector3 avg = Vector3.zero;
            // If we can't compute velocity yet due to lack of data, return nothing
            if (joint1.timestamps.Count == 0)
            {
                Debug.LogWarning("PoseHistory was asked for average velocity with zero or one recorded poses.");
                return avg;
            }

            Vector3[] arrayPositions1 = joint1.positions.ToArray();
            float[] arrayTimes1 = joint1.timestamps.ToArray();
            Vector3[] arrayPositions2 = joint2.positions.ToArray();
            float[] arrayTimes2 = joint2.timestamps.ToArray();
            
            for (int i = 1; i < joint1.positions.Count; i++)
            {
                Vector3 vel1 = (arrayPositions1[i] - arrayPositions1[i - 1]) 
                               / (arrayTimes1[i] - arrayTimes1[i - 1]);
                Vector3 vel2 = (arrayPositions2[i] - arrayPositions2[i - 1]) 
                               / (arrayTimes2[i] - arrayTimes2[i - 1]);
                avg += (1 / (float) joint1.positions.Count) 
                    * (vel1-vel2);
            }
            

            return avg;
        }
    }

    private void OnEnable()
    {
        thumbBaseJoint = new PoseHistory(trackedHandedness, thumbBase);
        thumbTipJoint = new PoseHistory(trackedHandedness, thumbTip);
        middleTipJoint = new PoseHistory(trackedHandedness, middleTip);
    }

    void LateUpdate()
    {
        // If the hand isn't available, do nothing
        IMixedRealityHand hand = GetController(trackedHandedness) as IMixedRealityHand;
        if (hand == null)// || !hand.TryGetJoint(trackedJoint, out MixedRealityPose pose))
        {
            //SetChildrenActive(false);
            snapState = SnapState.NotInitialized;
            return;
        } // If the hand has become available this frame, change snap state appropriately
        else if (snapState == SnapState.NotInitialized)
        {
            snapState = SnapState.NotSnapping;
        }
        //SetChildrenActive(true);
        //transform.position = pose.Position;
        //transform.rotation = pose.Rotation;
        
        // Execute updates
        thumbBaseJoint.DoUpdate();
        thumbTipJoint.DoUpdate();
        middleTipJoint.DoUpdate();
        
        // Process current state, switch if needed
        if (snapState == SnapState.NotSnapping)
        {
            if (snapReadyDistanceThreshold > PoseHistory.CurrentDistance(thumbTipJoint, middleTipJoint))
            {
                snapState = SnapState.SnapReady;
                indicatorRenderer.material.color = Color.blue;
            }
        }

        if (snapState == SnapState.SnapReady)
        {
            if (PoseHistory.AverageInterJointVelocity(thumbTipJoint, middleTipJoint).magnitude >
                snappingVelocityThreshold)
            {
                snapState = SnapState.Snapping;
                indicatorRenderer.material.color = Color.yellow;
            }
            else if (snapReadyDistanceThreshold * 1.5f < PoseHistory.CurrentDistance(thumbTipJoint, middleTipJoint))
            {
                snapState = SnapState.NotSnapping;
                indicatorRenderer.material.color = Color.red;
            }
        }

        if (snapState == SnapState.Snapping)
        {
            if (snapCompletedDistanceThreshold > PoseHistory.CurrentDistance(thumbBaseJoint, middleTipJoint))
            {
                snapState = SnapState.NotSnapping;
                FireSnapCompleted();
            }
        }
    }

    private void FireSnapCompleted()
    {
        indicatorRenderer.material.color = Color.green;
        
    }

    private void SetChildrenActive(bool isActive)
    {
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(isActive);
        }
    }

    private static IMixedRealityController GetController(Handedness handedness)
    {
        foreach (IMixedRealityController c in CoreServices.InputSystem.DetectedControllers)
        {
            if (c.ControllerHandedness.IsMatch(handedness))
            {
                return c;
            }
        }
        return null;
    }
}
