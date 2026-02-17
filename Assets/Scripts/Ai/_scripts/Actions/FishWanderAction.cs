using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rhinotap.StateMachine;

namespace Rhinotap.StateMachine
{
	[CreateAssetMenu(menuName = "StateMachine/Actions/FishWanderAction", fileName = "FishWanderAction")]
	public class FishWanderAction : StateAction
	{

#pragma warning disable 0414 // Hiding warnings for unused "deprecated" settings
        [Header("Wandering Curve Settings")]
        [SerializeField]
        private AnimationCurve curve;

        [Range(0f, 1f)]
        [SerializeField]
        private float curveIntensity = 0.05f;

        [SerializeField]
        private AnimationCurve easing =  AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
#pragma warning restore 0414

        [Header("Movement Radius Settings")]
        [Space(20)]
        [SerializeField]
        private float MovementRadiusX = 1f;
        [SerializeField]
        private float MovementRadiusY = 1f;

        [Header("AI Behavior Settings")]
        [SerializeField]
        private int minWanderCount = 1;
        [SerializeField]
        private int maxWanderCount = 3;


        public override void OnUpdate(StateController Controller)
		{
            Initialize(Controller as StateController);

            // Schooling Override
            if (Controller.FishController.school != null)
            {
                MoveWithSchool(Controller);
                return;
            }

            if( DistanceToDestination(Controller) <= 0.5f)
            {
                SetDestination(Controller);
            }

            MoveTowards(Controller);

		}



        private void MoveWithSchool(StateController Controller)
        {
            FishSchool school = Controller.FishController.school;
            // School destination is the general target. Add offset for formation.
            Vector2 target = school.CurrentDestination + Controller.FishController.formationOffset;

            // Move logic similar to MoveTowards
            float speed = Controller.FishController.Speed;
            Vector2 currentPos = Controller.transform.localPosition;
            Vector2 direction = (target - currentPos).normalized;
            Vector2 newPos = currentPos + direction * speed * Time.deltaTime;

            // Flip towards target
            Controller.FishController.FlipTowardsDestination(target);

            Controller.transform.localPosition = newPos;

            // Cleanup: Destroy if out of bounds (passed the destination or far off screen)
            if (Mathf.Abs(newPos.x) > 45f) 
            {
                Controller.FishController.DespawnSelf();
            }
        }

        /// <summary>
        /// Set required data for this controller (Will run once)
        /// </summary>
        /// <param name="Controller"></param>
        private void Initialize(StateController Controller)
        {
            if (Controller.GetData<bool>("isInitialized") == true) return;

            //Set required data
            Controller.SetData<Vector2?>("CurrentDestination", null);

            Controller.SetData<float>("MovementTimeElapsed", 0f);

            Controller.SetData<bool>("isInitialized", true);

            Controller.SetData<int>("wanderingActionsCompleted", 0);
            
            // Randomly decide how many times to change direction inside the arena before leaving
            int wanderTimes = Random.Range(minWanderCount, maxWanderCount + 1);
            Controller.SetData<int>("targetWanderCount", wanderTimes);
        }


        /// <summary>
        /// Get a random Vector2 in given radius
        /// </summary>
        /// <returns></returns>
        private Vector2 RandomDestination()
        {
            Vector2 result = Vector2.zero;

            result.x = Random.Range(-MovementRadiusX, MovementRadiusX);
            result.y = Random.Range(-MovementRadiusY, MovementRadiusY);

            return result;
        }

        /// <summary>
        /// Distance to current Destination (Local Space)
        /// </summary>
        /// <param name="Controller"></param>
        /// <returns></returns>
        private float DistanceToDestination(StateController Controller)
        {
            Vector2? destination = Controller.GetData<Vector2?>("CurrentDestination");
            if (destination == null) return 0f;

            int DestinationReachedCount = Controller.GetData<int>("wanderingActionsCompleted");

            float distance =  Vector2.Distance((Vector2)Controller.transform.localPosition, destination.Value);

            if( distance <= 1.0f) // Increased threshold slightly for better hit detection
            {
                Controller.SetData<int>("wanderingActionsCompleted", DestinationReachedCount+1);
                RemoveDestination(Controller);
            }
            return distance;
        }


        /// <summary>
        /// Set the current destination (Local Space)
        /// </summary>
        /// <param name="Controller"></param>
        private void SetDestination(StateController Controller)
        {
            int currentWanderCount = Controller.GetData<int>("wanderingActionsCompleted");
            int targetWanderCount = Controller.GetData<int>("targetWanderCount");
            
            Vector2 newDestination;

            // Check if we should wander inside or exit
            if (currentWanderCount < targetWanderCount)
            {
                // Wander Inside the Arena (Thrill Factor)
                // Pick a random point within the visible area (approx -20 to 20 X, -12 to 12 Y)
                float targetX = Random.Range(-20f, 20f);
                float targetY = Random.Range(-12f, 12f);
                newDestination = new Vector2(targetX, targetY);
            }
            else
            {
                // Time to Leave: Exit Logic
                // If currently on left, go right exit. If on right, go left exit.
                // Or just keep going in current direction to exit.
                // Let's use current position to determine nearest or natural exit.
                
                // If moving Right (positive velocity or destination), exit Right (+45)
                // If moving Left, exit Left (-45)
                // Since we don't track velocity easily here, let's use current facing or position logic.
                
                // Simple logic: If on left side, exit right. If on right side, exit left.
                // This ensures they cross the screen one last time.
                float targetX = (Controller.transform.localPosition.x < 0) ? 45f : -45f;
                float targetY = Controller.transform.localPosition.y + Random.Range(-2f, 2f); 
                newDestination = new Vector2(targetX, targetY);
            }

            //Set destination
            Controller.SetData<Vector2?>("CurrentDestination", newDestination);

            //Reset movement timer
            Controller.SetData<float>("MovementTimeElapsed", 0f);

            //Flip fish towards new destination
            Controller.FishController.FlipTowardsDestination(newDestination);
        }

        /// <summary>
        /// Null the destination value
        /// </summary>
        /// <param name="Controller"></param>
        private void RemoveDestination(StateController Controller)
        {
            Controller.SetData<Vector2?>("CurrentDestination", null);
            Controller.SetData<float>("MovementTimeElapsed", 0f);
        }



        private void MoveTowards(StateController Controller)
        {
            if( Controller.GetData<Vector2?>("CurrentDestination") == null ) { return; }
            Vector2 destination = Controller.GetData<Vector2?>("CurrentDestination").Value;
            
            // Simple linear movement towards destination
            float speed = Controller.FishController.Speed;
            Vector2 currentPos = Controller.transform.localPosition;
            
            // Move towards destination
            Vector2 direction = (destination - currentPos).normalized;
            Vector2 newPos = currentPos + direction * speed * Time.deltaTime;

            Controller.transform.localPosition = newPos;

            // Cleanup: Destroy if out of bounds (passed the destination or far off screen)
            if (Mathf.Abs(newPos.x) > 40f)
            {
                // Destroy the fish entirely as it has left the screen
                Controller.FishController.DespawnSelf();
            }
        }



	}
}
