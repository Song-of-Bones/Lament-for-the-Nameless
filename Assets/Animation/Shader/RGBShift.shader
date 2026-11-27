Shader "UI/RGBGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Phase ("Phase", Float) = 0
        _Shift ("Channel Shift", Float) = 0.004
        _RedShift ("Red Shift", Float) = 1
        _GreenShift ("Green Shift", Float) = 0.35
        _GreenVertical ("Green Vertical", Float) = 0.12
        _BlueShift ("Blue Shift", Float) = 1
        _Jitter ("Jitter", Float) = 0.0015
        _TearChance ("Row Tear Chance", Range(0,1)) = 0.05
        _TearAmount ("Row Tear Amount", Float) = 0.02
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

            sampler2D _MainTex;
            fixed4 _Color;
            float _Phase;
            float _Shift;
            float _RedShift;
            float _GreenShift;
            float _GreenVertical;
            float _BlueShift;
            float _Jitter;
            float _TearChance;
            float _TearAmount;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
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
                float2 uv = i.texcoord;
                float t = floor(_Phase);

                float row = floor(uv.y * 140.0);
                float tearRand = hash21(float2(row, t));
                float tearOffset = tearRand > (1.0 - _TearChance)
                    ? (hash21(float2(t, row)) - 0.5) * 2.0 * _TearAmount
                    : 0.0;
                uv.x += tearOffset;

                float jitter = (hash21(float2(t, 3.7)) - 0.5) * 2.0 * _Jitter;
                float2 baseOff = float2(_Shift + jitter, 0.0);

                // 三原色分离：红向右、蓝向左、绿小幅右移并轻微下移
                float2 offR = baseOff * _RedShift;
                float2 offG = float2(baseOff.x * _GreenShift, baseOff.x * _GreenVertical);
                float2 offB = -baseOff * _BlueShift;

                float ar = tex2D(_MainTex, uv + offR).a;
                float ag = tex2D(_MainTex, uv + offG).a;
                float ab = tex2D(_MainTex, uv + offB).a;

                float a = max(ar, max(ag, ab));
                fixed3 col = fixed3(ar, ag, ab) * i.color.rgb;
                return fixed4(col, a * i.color.a);
            }
            ENDCG
        }
    }
}
