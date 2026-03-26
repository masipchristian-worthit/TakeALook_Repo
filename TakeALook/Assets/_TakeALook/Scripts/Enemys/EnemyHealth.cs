using UnityEngine;

public class EnemyHealth : MonoBehaviour
{

    [Header("Health System Configuration")]
    [SerializeField] int health; //Vida actual del enemigo
    [SerializeField] int maxHealth; //Vida máxima del enemigo

    [Header("Feedback Configuration")]
    [SerializeField] Material damagedMat; //Ref al material que da Feedback de dañado
    [SerializeField] MeshRenderer enemyRend; //Ref al renderer del modelo del enemigo
    [SerializeField] GameObject deathVfx; //Ref al sistema de partículas de muerte
    Material baseMat; //Ref al material base del modelo del enemigo

    private void Awake()
    {
        health = maxHealth; //Cuando se genera el enemigo su vida actual se carga a la máxima
        baseMat = enemyRend.material; //Se almacena el material base del modelo del enemigo
    }

    // Update is called once per frame
    void Update()
    {
        if (health <= 0)
        {
            health = 0; //La vida no puede bajar de 0
            deathVfx.SetActive(true); //Encendemos el VFX de muerte
            deathVfx.transform.position = transform.position; //Ponemos el vfx en la posición del enemigo
            gameObject.SetActive(false); //Se apaga el enemigo = "muere"
        }
    }

    public void TakeDamage(int damage)
    {
        health -= damage; //Quitar tanta vida como valor de daño viene de fuera
        enemyRend.material = damagedMat; //Se cambia temporalmente el material base por el material dañado
        Invoke(nameof(ResetEnemyMat), 0.1f); //Llamar al reseteo del material con 0.1 segundos de espera
    }

    void ResetEnemyMat()
    {
        enemyRend.material = baseMat; //Cambiar el material del modelo al material base
    }

}
