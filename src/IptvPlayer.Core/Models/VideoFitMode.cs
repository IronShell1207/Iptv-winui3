namespace IptvPlayer.Core.Models;

/// <summary>Как кадр вписывается в окно плеера.</summary>
public enum VideoFitMode
{
    /// <summary>Целиком, с полями по краям — обычный режим.</summary>
    Fit,

    /// <summary>Заполнить окно, обрезав лишнее по краям.</summary>
    Crop,

    /// <summary>Растянуть на всё окно, не сохраняя пропорции.</summary>
    Stretch,

    /// <summary>Оригинальный размер кадра, без масштабирования.</summary>
    Original,
}
