using UnityEngine;

/// Short-lived debris piece.
public class Debris : MonoBehaviour
{
    public float life = 2.5f;
    void Update()
    {
        life -= Time.deltaTime;
        if (life < 0.5f) transform.localScale *= Mathf.Pow(0.02f, Time.deltaTime);
        if (life <= 0f) Destroy(gameObject);
    }
}
