Shader "UI/SnowNoise"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Phase ("Phase", Float) = 0
        _Density ("Noise Density", Float) = 620
        _Threshold ("Threshold", Range(0,1)) = 0.94
        _BandAmount ("Band Amount", Range(0,1)) = 0.10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            float _Phase;
            float _Density;
            float _Threshold;
            float _BandAmount;

            float hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 aspect = float2(_ScreenParams.x / max(_ScreenParams.y, 1.0), 1.0);
                float2 cell = floor(i.texcoord * _Density * aspect);

                // 每个粒子拥有独立的相位与刷新频率，随机出现/消失，不朝统一方向滚动
                float r1 = hash21(cell);
                float r2 = hash21(cell + 47.1);
                float rate = 5.0 + r2 * 28.0;
                float t = floor(_Phase * rate + r1 * 97.0);
                float n = hash21(cell + float2(t * 13.0, t * 7.0));
                float grain = smoothstep(_Threshold, 1.0, n);

                // 竖向干扰条，同样按列独立随机
                float col = floor(i.texcoord.x * 80.0);
                float band = step(0.945, hash21(float2(col * 1.7, floor(_Phase * (7.0 + col * 0.13)))));

                float a = saturate(grain + band * _BandAmount) * i.color.a;
                return fixed4(i.color.rgb, a);
            }
            ENDCG
        }
    }
}
