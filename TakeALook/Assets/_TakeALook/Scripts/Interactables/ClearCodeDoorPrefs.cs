using UnityEngine;

public class ClearCodeDoorPrefs : MonoBehaviour
{
    private void Start()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        Debug.Log("PlayerPrefs borrados. Todas las puertas con código vuelven a estar bloqueadas.");
    }
}