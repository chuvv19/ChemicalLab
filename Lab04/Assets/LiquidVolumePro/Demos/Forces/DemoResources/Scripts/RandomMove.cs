using UnityEngine;
using System.Collections;

namespace LiquidVolumeFX
{
	public class RandomMove : MonoBehaviour
	{

		[Range(-10,10f)]
		public float right = 2f;

		[Range(-10,10f)]
		public float left = -2f;
		
		[Range(-10,10f)]
		public float back = 2f;
		
		[Range(-10,10f)]
		public float front = -1f;

		[Range(0,0.2f)]
		public float speed = 0.5f;


        [Range(0, 2f)]
        public float rotationSpeed = 1f;

		[Range(0.1f, 2f)]
		public float randomSpeed;

		public bool automatic;


		Vector3 velocity = Vector3.zero;
		int flaskType = 0;

		void OnEnable() {
			InputProxy.SetupEventSystem();
		}

		void Update ()
		{
			if (InputProxy.GetKeyDown(KeyCode.F)) {
				flaskType++;
				if (flaskType>=3) flaskType = 0;
				transform.Find("SphereFlask").gameObject.SetActive(flaskType==0);
				transform.Find("CylinderFlask").gameObject.SetActive(flaskType==1);
				transform.Find("CubeFlask").gameObject.SetActive(flaskType==2);
			}

			Vector3 accel = Vector3.zero;

			if (automatic) {
				if (Random.value > 0.99f) {
					accel = Vector3.right * (speed + (Random.value - 0.5f) * randomSpeed);
				}
			} else {
				if (InputProxy.GetKey(KeyCode.RightArrow)) {
					accel += Vector3.right * speed;
				}
				if (InputProxy.GetKey(KeyCode.LeftArrow)) {
					accel += Vector3.left * speed;
				}
				if (InputProxy.GetKey(KeyCode.UpArrow)) {
					accel += Vector3.forward * speed;
				}
				if (InputProxy.GetKey(KeyCode.DownArrow)) {
					accel += Vector3.back * speed;
				}
			}
			float deltaTime = 60f * Time.deltaTime;
			velocity += accel * deltaTime;
			const float damping = 0.005f;
			float dampAmount = damping * deltaTime;
			if (velocity.magnitude > dampAmount)
			{
				velocity -= velocity.normalized * dampAmount;
			}
			else {
				velocity = Vector3.zero;
			} 

			transform.localPosition += velocity * deltaTime;

                if (InputProxy.GetKey(KeyCode.W)) {
                    transform.Rotate(0, 0, rotationSpeed * deltaTime);
                } else if (InputProxy.GetKey(KeyCode.S)) {
                    transform.Rotate(0, 0, -rotationSpeed * deltaTime);
                }

			if (transform.localPosition.x > right) {
				transform.localPosition = new Vector3(right, transform.localPosition.y, transform.localPosition.z);
				velocity.Set(0, 0, 0);
			}
			if (transform.localPosition.x < left) {
				transform.localPosition = new Vector3(left, transform.localPosition.y, transform.localPosition.z);
				velocity.Set(0, 0, 0);
			}
			if (transform.localPosition.z > back) {
				transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y, back);
				velocity.Set(0, 0, 0);
			}
			if (transform.localPosition.z < front) {
				transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y, front);
				velocity.Set(0, 0, 0);
			}

		}
	}

}